using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WebGallery.Models;

namespace WebGallery.Services;

public sealed class MediaService
{
    public const double MaxSegmentDurationSeconds = 12;

    private const int MaxSegmentBytes = 128 * 1024 * 1024;
    private const int MaxSubtitleBytes = 32 * 1024 * 1024;
    private const int MaxConcurrentMediaJobs = 16;
    private const int MaxConcurrentQuickSyncJobs = 2;
    private const int EncoderThreads = 16;
    // Segments are encoded independently and appended to one MSE timeline.
    // B-frame reordering gives video a negative decode timestamp while copied
    // audio starts at zero; correcting that with one fragment-wide timestamp
    // offset makes audio lead the picture at every join.  Zero B-frames keeps
    // the audio/video clocks aligned and avoids periodic video stalls.
    private const int EncoderBFrames = 0;
    private const int NvidiaPresentationDelayFrames = 0;
    private const int QuickSyncPresentationDelayFrames = 0;
    private const int X264PresentationDelayFrames = 0;
    private const double KeyframeBucketSeconds = 60;
    private const double KeyframeLookBehindSeconds = 60;
    private const double KeyframeReadDurationSeconds = 120;
    private const double CopySegmentEndGuardSeconds = 0.001;
    private static readonly HashSet<string> TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "subrip",
        "ass",
        "ssa",
        "webvtt",
        "mov_text"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GalleryOptions _options;
    private readonly ILogger<MediaService> _logger;
    private readonly SemaphoreSlim _segmentSlots = new(MaxConcurrentMediaJobs, MaxConcurrentMediaJobs);
    private readonly SemaphoreSlim _quickSyncSlots = new(MaxConcurrentQuickSyncJobs, MaxConcurrentQuickSyncJobs);
    private readonly SemaphoreSlim _nvidiaProbeLock = new(1, 1);
    private readonly SemaphoreSlim _quickSyncProbeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ProbeDocument> _probeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<KeyframePoint>> _keyframeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SubtitleProgressState> _subtitleProgress = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _quickSyncDecodeFailures = new(StringComparer.OrdinalIgnoreCase);
    private int _nvidiaEncoderState = -1;
    private int _quickSyncEncoderState = -1;

    public MediaService(IOptions<GalleryOptions> options, ILogger<MediaService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MediaMetadataDto> GetMetadataAsync(string path, CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(path, cancellationToken);
        var video = probe.Streams.Where(stream => IsType(stream, "video")).ToArray();
        if (video.Length == 0)
        {
            throw new MediaPlaybackException("This file has no playable video stream.", 415);
        }

        var audio = probe.Streams.Where(stream => IsType(stream, "audio")).ToArray();
        var subtitles = probe.Streams.Where(stream => IsType(stream, "subtitle")).ToArray();
        var canStreamWithoutTranscode = IsBrowserCompatibleVideo(video[0])
            && (audio.Length == 0 || audio.Any(IsBrowserCompatibleAudio));
        var warning = canStreamWithoutTranscode
            ? null
            : "This video will transcode only the buffered or sought segments.";

        return new MediaMetadataDto(
            ParseDuration(probe.Format?.Duration),
            video.Select(ToTrack).ToArray(),
            audio.Select(ToTrack).ToArray(),
            subtitles.Select(ToTrack).ToArray(),
            ParsePositiveLong(video[0].BitRate) ?? ParsePositiveLong(probe.Format?.BitRate),
            canStreamWithoutTranscode,
            warning);
    }

    public async Task<byte[]> GetSubtitleAsync(
        string path,
        int subtitleIndex,
        string? progressId,
        CancellationToken cancellationToken)
    {
        var progress = BeginSubtitleProgress(progressId);
        try
        {
            var probe = await ProbeAsync(path, cancellationToken);
            progress?.SetTotalSeconds(ParseDuration(probe.Format?.Duration));
            var subtitle = probe.Streams.FirstOrDefault(stream =>
                IsType(stream, "subtitle") && stream.Index == subtitleIndex);
            if (subtitle is null || !IsSupportedSubtitle(subtitle))
            {
                throw new MediaPlaybackException("The selected subtitle track is not supported by the web player.", 400);
            }

            using var process = new Process
            {
                StartInfo = BuildStartInfo(_options.FfmpegPath, new[]
                {
                    "-hide_banner",
                    "-loglevel", "error",
                    "-nostdin",
                    "-nostats",
                    "-stats_period", "0.25",
                    "-progress", "pipe:2",
                    "-i", path,
                    "-map", $"0:{subtitle.Index}",
                    "-vn",
                    "-an",
                    "-dn",
                    "-c:s", "webvtt",
                    "-map_metadata", "-1",
                    "-map_chapters", "-1",
                    "-f", "webvtt",
                    "pipe:1"
                }, redirectStandardOutput: true),
                EnableRaisingEvents = true
            };

            try
            {
                StartProcess(process);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                throw new MediaPlaybackException("Video playback tools are unavailable on this server.", 503, ex);
            }

            var outputTask = ReadOutputAsync(process.StandardOutput.BaseStream, cancellationToken, MaxSubtitleBytes);
            var errorTask = ReadSubtitleProgressAsync(process.StandardError, progress, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                var output = await outputTask;
                var error = await errorTask;
                if (process.ExitCode != 0 || output.Length == 0)
                {
                    _logger.LogWarning("FFmpeg subtitle exited with code {ExitCode}: {Error}", process.ExitCode, error.Trim());
                    throw new MediaPlaybackException("This subtitle track could not be prepared.", 415);
                }

                progress?.Complete();
                return output;
            }
            finally
            {
                StopProcess(process);
            }
        }
        catch (OperationCanceledException)
        {
            progress?.Cancel();
            throw;
        }
        catch (MediaPlaybackException)
        {
            progress?.Fail();
            throw;
        }
        catch (Exception ex)
        {
            progress?.Fail();
            _logger.LogWarning(ex, "Could not prepare subtitle track for {SourcePath}", path);
            throw new MediaPlaybackException("This subtitle track could not be prepared.", 503, ex);
        }
    }

    public SubtitleProgressDto? GetSubtitleProgress(string? progressId)
    {
        CleanupSubtitleProgress();
        var normalized = NormalizeProgressId(progressId);
        return normalized is not null && _subtitleProgress.TryGetValue(normalized, out var progress)
            ? progress.Snapshot()
            : null;
    }

    private SubtitleProgressState? BeginSubtitleProgress(string? progressId)
    {
        CleanupSubtitleProgress();
        var normalized = NormalizeProgressId(progressId);
        if (normalized is null) return null;
        var progress = new SubtitleProgressState(normalized);
        _subtitleProgress[normalized] = progress;
        return progress;
    }

    private void CleanupSubtitleProgress()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        foreach (var entry in _subtitleProgress)
        {
            if (entry.Value.IsStale(cutoff)) _subtitleProgress.TryRemove(entry.Key, out _);
        }
    }

    private static string? NormalizeProgressId(string? progressId) =>
        Guid.TryParse(progressId, out var parsed) ? parsed.ToString("N") : null;

    private static async Task<string> ReadSubtitleProgressAsync(
        StreamReader reader,
        SubtitleProgressState? progress,
        CancellationToken cancellationToken)
    {
        var errors = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            {
                progress?.ReportSeconds(Math.Max(0, microseconds / 1_000_000d));
            }
            else if (!IsFfmpegProgressLine(line))
            {
                if (errors.Length > 0) errors.AppendLine();
                errors.Append(line);
            }
        }
        return errors.ToString();
    }

    private static bool IsFfmpegProgressLine(string line) =>
        line.StartsWith("frame=", StringComparison.Ordinal)
        || line.StartsWith("fps=", StringComparison.Ordinal)
        || line.StartsWith("stream_", StringComparison.Ordinal)
        || line.StartsWith("bitrate=", StringComparison.Ordinal)
        || line.StartsWith("total_size=", StringComparison.Ordinal)
        || line.StartsWith("out_time_ms=", StringComparison.Ordinal)
        || line.StartsWith("out_time=", StringComparison.Ordinal)
        || line.StartsWith("dup_frames=", StringComparison.Ordinal)
        || line.StartsWith("drop_frames=", StringComparison.Ordinal)
        || line.StartsWith("speed=", StringComparison.Ordinal)
        || line.StartsWith("progress=", StringComparison.Ordinal);

    public async Task<MediaSegmentResult> GetSegmentAsync(
        string path,
        int? audioIndex,
        int? subtitleIndex,
        double startSeconds,
        double durationSeconds,
        CancellationToken cancellationToken,
        bool allowHevc = false)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new MediaPlaybackException("The segment start is invalid.", 400);
        }
        if (!double.IsFinite(durationSeconds)
            || durationSeconds <= 0
            || durationSeconds > MaxSegmentDurationSeconds)
        {
            throw new MediaPlaybackException("The segment duration is invalid.", 400);
        }

        var tracks = await ResolveTracksAsync(path, audioIndex, subtitleIndex, cancellationToken);

        await _segmentSlots.WaitAsync(cancellationToken);
        try
        {
            var copyVideo = IsBrowserCompatibleVideo(tracks.Video, allowHevc);
            // Every independently appended stream-copy fragment must start on a
            // decodable random-access point. The browser places these keyframe-aligned
            // fragments on a continuous MSE timeline.
            var copyPlan = copyVideo
                ? await ResolveCopySegmentPlanAsync(tracks.SourcePath, startSeconds, durationSeconds, cancellationToken)
                : null;
            var effectiveStart = copyPlan?.StartSeconds ?? startSeconds;
            var effectiveDuration = copyPlan?.DurationSeconds ?? durationSeconds;
            var seekPlan = copyPlan is not null
                ? new SeekPlan(
                    copyPlan.InputStartSeconds,
                    Math.Max(0, copyPlan.DecodeStartSeconds - copyPlan.InputStartSeconds))
                : startSeconds > 0
                    ? await ResolveSeekPlanAsync(tracks, startSeconds, cancellationToken)
                    : null;
            var encoderMode = copyVideo
                ? VideoEncoderMode.Software
                : await SelectPreferredEncoderAsync(tracks, cancellationToken);
            var result = await RunSegmentFfmpegAsync(
                tracks, effectiveStart, effectiveDuration, seekPlan, encoderMode, allowHevc, cancellationToken);
            if (!result.Succeeded && encoderMode == VideoEncoderMode.Nvidia)
            {
                Volatile.Write(ref _nvidiaEncoderState, 0);
                _logger.LogWarning(
                    "NVENC segment failed with code {ExitCode}; disabling NVENC until process restart and retrying the same segment with the next encoder: {Error}",
                    result.ExitCode,
                    result.Error.Trim());
                encoderMode = await SelectQuickSyncOrSoftwareAsync(tracks, cancellationToken);
                result = await RunSegmentFfmpegAsync(
                    tracks, effectiveStart, effectiveDuration, seekPlan, encoderMode, allowHevc, cancellationToken);
            }
            if (!result.Succeeded && encoderMode == VideoEncoderMode.QuickSyncHardware)
            {
                _quickSyncDecodeFailures.TryAdd(GetProbeKey(tracks.SourcePath), 0);
                _logger.LogWarning(
                    "Quick Sync hardware decode/encode failed with code {ExitCode}; retrying the same segment with Quick Sync encode and software decode: {Error}",
                    result.ExitCode,
                    result.Error.Trim());
                encoderMode = VideoEncoderMode.QuickSync;
                result = await RunSegmentFfmpegAsync(
                    tracks, effectiveStart, effectiveDuration, seekPlan, encoderMode, allowHevc, cancellationToken);
            }
            if (!result.Succeeded && encoderMode == VideoEncoderMode.QuickSync)
            {
                Volatile.Write(ref _quickSyncEncoderState, 0);
                _logger.LogWarning(
                    "Quick Sync encode failed with code {ExitCode}; disabling Quick Sync until process restart and retrying the same segment with libx264: {Error}",
                    result.ExitCode,
                    result.Error.Trim());
                encoderMode = VideoEncoderMode.Software;
                result = await RunSegmentFfmpegAsync(
                    tracks, effectiveStart, effectiveDuration, seekPlan, encoderMode, allowHevc, cancellationToken);
            }
            if (!result.Succeeded)
            {
                _logger.LogWarning("FFmpeg segment exited with code {ExitCode}: {Error}", result.ExitCode, result.Error.Trim());
                throw new MediaPlaybackException("This video segment could not be prepared.", 415);
            }

            var presentationLead = copyPlan is not null
                ? Math.Max(0, copyPlan.StartSeconds - copyPlan.DecodeStartSeconds)
                : copyVideo
                    ? 0
                    : EstimateEncoderPresentationLead(tracks, result.Info.Mode);
            return new MediaSegmentResult(
                result.Output,
                result.Info.Mode,
                result.Info.OutputCodec,
                result.Info.Profile,
                result.Info.Level,
                result.Info.Quality,
                result.Info.TargetBitRate,
                result.Info.MaxBitRate,
                effectiveStart,
                presentationLead,
                copyPlan?.NextStartSeconds ?? startSeconds + durationSeconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MediaPlaybackException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not prepare an in-memory media segment for {SourcePath}", path);
            throw new MediaPlaybackException("This video segment could not be prepared.", 503, ex);
        }
        finally
        {
            _segmentSlots.Release();
        }
    }

    private async Task<FfmpegResult> RunSegmentFfmpegAsync(
        SelectedTracks tracks,
        double startSeconds,
        double durationSeconds,
        SeekPlan? seekPlan,
        VideoEncoderMode encoderMode,
        bool allowHevc,
        CancellationToken cancellationToken)
    {
        var useQuickSync = encoderMode is VideoEncoderMode.QuickSync or VideoEncoderMode.QuickSyncHardware;
        if (useQuickSync) await _quickSyncSlots.WaitAsync(cancellationToken);
        try
        {
            using var process = StartFfmpeg(tracks, startSeconds, durationSeconds, seekPlan, encoderMode, allowHevc);
            var info = DescribeSegment(tracks, encoderMode, allowHevc);
            try
            {
                StartProcess(process);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                var outputTask = ReadOutputAsync(process.StandardOutput.BaseStream, cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var output = await outputTask;
                var error = await stderrTask;
                return new FfmpegResult(process.ExitCode, output, error, info);
            }
            finally
            {
                StopProcess(process);
            }
        }
        finally
        {
            if (useQuickSync) _quickSyncSlots.Release();
        }
    }

    // Kept as a compatibility fallback for older pages. The new player uses
    // GetSegmentAsync and MediaSource so it only asks for the time range it needs.
    public async Task StreamAsync(
        HttpContext context,
        string path,
        int? audioIndex,
        int? subtitleIndex,
        CancellationToken cancellationToken)
    {
        var tracks = await ResolveTracksAsync(path, audioIndex, subtitleIndex, cancellationToken);

        await _segmentSlots.WaitAsync(cancellationToken);
        Process? process = null;
        try
        {
            process = StartFfmpeg(tracks, null, null, null);
            StartProcess(process);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "video/mp4";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(path))}.mp4";

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg stream exited with code {ExitCode}: {Error}", process.ExitCode, stderr.Trim());
            }
        }
        finally
        {
            StopProcess(process);
            _segmentSlots.Release();
        }
    }

    public async Task StreamContinuousHevcAsync(
        HttpContext context,
        string path,
        int? audioIndex,
        double startSeconds,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new MediaPlaybackException("The stream start is invalid.", 400);
        }

        var tracks = await ResolveTracksAsync(path, audioIndex, null, cancellationToken);
        if (!IsHevcVideo(tracks.Video) || !IsBrowserCompatibleVideo(tracks.Video, allowHevc: true))
        {
            throw new MediaPlaybackException("Continuous direct streaming is only available for compatible HEVC video.", 415);
        }

        var copyPlan = await ResolveCopySegmentPlanAsync(tracks.SourcePath, startSeconds, 8, cancellationToken);
        var seekPlan = new SeekPlan(
            copyPlan.InputStartSeconds,
            Math.Max(0, copyPlan.DecodeStartSeconds - copyPlan.InputStartSeconds));

        await _segmentSlots.WaitAsync(cancellationToken);
        Process? process = null;
        try
        {
            process = StartFfmpeg(
                tracks,
                copyPlan.StartSeconds,
                durationSeconds: null,
                seekPlan,
                encoderMode: VideoEncoderMode.Software,
                allowHevc: true);
            StartProcess(process);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "video/mp4";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Media-Mode"] = "copy-hevc-continuous";
            context.Response.Headers["X-Media-Source-Start"] = copyPlan.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            context.Response.Headers["X-Media-Presentation-Lead"] = Math.Max(0, copyPlan.StartSeconds - copyPlan.DecodeStartSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            await context.Response.StartAsync(cancellationToken);

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardOutput.BaseStream.CopyToAsync(context.Response.Body, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Continuous HEVC stream exited with code {ExitCode}: {Error}", process.ExitCode, stderr.Trim());
            }
        }
        finally
        {
            StopProcess(process);
            _segmentSlots.Release();
        }
    }

    private async Task<SelectedTracks> ResolveTracksAsync(
        string path,
        int? audioIndex,
        int? subtitleIndex,
        CancellationToken cancellationToken)
    {
        var probe = await ProbeAsync(path, cancellationToken);
        var video = probe.Streams.FirstOrDefault(stream => IsType(stream, "video"));
        if (video is null)
        {
            throw new MediaPlaybackException("This file has no playable video stream.", 415);
        }

        var audioStreams = probe.Streams.Where(stream => IsType(stream, "audio")).ToArray();
        var subtitleStreams = probe.Streams.Where(stream => IsType(stream, "subtitle")).ToArray();
        var audio = audioIndex.HasValue
            ? audioStreams.FirstOrDefault(stream => stream.Index == audioIndex.Value)
            : audioStreams.FirstOrDefault(stream => IsDefault(stream)) ?? audioStreams.FirstOrDefault();
        if (audioIndex.HasValue && audio is null)
        {
            throw new MediaPlaybackException("The selected audio track is not available.", 400);
        }

        var subtitle = subtitleIndex.HasValue
            ? subtitleStreams.FirstOrDefault(stream => stream.Index == subtitleIndex.Value)
            : null;
        if (subtitleIndex.HasValue && (subtitle is null || !IsSupportedSubtitle(subtitle)))
        {
            throw new MediaPlaybackException("The selected subtitle track is not supported by the web player.", 400);
        }

        return new SelectedTracks(path, video, audio, subtitle, probe.Format?.BitRate);
    }

    private Process StartFfmpeg(
        SelectedTracks tracks,
        double? startSeconds,
        double? durationSeconds,
        SeekPlan? seekPlan,
        VideoEncoderMode encoderMode = VideoEncoderMode.Software,
        bool allowHevc = false)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-threads", EncoderThreads.ToString(CultureInfo.InvariantCulture),
            "-filter_threads", EncoderThreads.ToString(CultureInfo.InvariantCulture)
        };
        if (seekPlan is SeekPlan fastSeek && fastSeek.InputSeconds > 0)
        {
            // Input seeking lets FFmpeg jump to the nearest indexed keyframe.
            // The residual output seek below trims the keyframe pre-roll for
            // both the copy and transcode paths.
            arguments.AddRange(["-ss", FormatSeconds(fastSeek.InputSeconds)]);
        }
        if (encoderMode == VideoEncoderMode.QuickSyncHardware)
        {
            // Keep decoded frames on the Intel media device so HEVC/AV1 input
            // does not consume the low-power CPU before h264_qsv encodes it.
            // If this decoder path is unsupported for a particular file, the
            // caller retries the same segment with software decode + QSV encode.
            arguments.AddRange(["-hwaccel", "qsv", "-hwaccel_output_format", "qsv"]);
        }
        arguments.AddRange(["-i", tracks.SourcePath, "-map", $"0:{tracks.Video.Index}"]);
        if (tracks.Audio is not null)
        {
            arguments.AddRange(["-map", $"0:{tracks.Audio.Index}"]);
        }
        if (tracks.Subtitle is not null)
        {
            arguments.AddRange(["-map", $"0:{tracks.Subtitle.Index}"]);
        }
        if (seekPlan is SeekPlan preciseSeek && preciseSeek.OutputSeconds > 0)
        {
            arguments.AddRange(["-ss", FormatSeconds(preciseSeek.OutputSeconds)]);
        }
        if (durationSeconds is double duration)
        {
            arguments.AddRange(["-t", FormatSeconds(duration)]);
        }

        if (IsBrowserCompatibleVideo(tracks.Video, allowHevc))
        {
            arguments.AddRange(["-c:v", "copy"]);
            if (IsHevcVideo(tracks.Video))
            {
                // Chrome/Safari expect the hvc1 sample entry for HEVC carried
                // in fragmented MP4. The source remains bit-for-bit copied.
                arguments.AddRange(["-tag:v", "hvc1"]);
            }
            _logger.LogDebug("Stream-copying {Codec} video for this browser session", tracks.Video.CodecName);
        }
        else
        {
            var encoding = SelectVideoEncoding(tracks);
            // Keep the fallback browser-compatible and scoped to this one
            // requested segment. FFmpeg still writes only to stdout; no
            // intermediate or cache file is created. Constrained CRF retains
            // detail in strong sources while its source-aware VBV ceiling
            // avoids spending excessive bitrate on already-low-quality video.
            if (encoderMode == VideoEncoderMode.Nvidia)
            {
                var averageRate = RoundBitRate(encoding.MaxRateBitsPerSecond * 4 / 5);
                arguments.AddRange([
                    "-c:v", "h264_nvenc",
                    "-preset", "p4",
                    "-tune", "hq",
                    "-profile:v", encoding.Profile,
                    "-level:v", encoding.Level,
                    "-rc:v", "vbr",
                    "-cq:v", encoding.Crf.ToString(CultureInfo.InvariantCulture),
                    "-b:v", FormatKbps(averageRate),
                    "-maxrate:v", FormatKbps(encoding.MaxRateBitsPerSecond),
                    "-bufsize:v", FormatKbps(encoding.BufferBitsPerSecond),
                    "-spatial-aq", "1",
                    "-temporal-aq", "1",
                    "-rc-lookahead", "20",
                    "-bf", EncoderBFrames.ToString(CultureInfo.InvariantCulture),
                    "-gpu", "any",
                    "-pix_fmt", "yuv420p"
                ]);
            }
            else if (encoderMode is VideoEncoderMode.QuickSync or VideoEncoderMode.QuickSyncHardware)
            {
                var averageRate = RoundBitRate(encoding.MaxRateBitsPerSecond * 4 / 5);
                if (encoderMode == VideoEncoderMode.QuickSyncHardware)
                {
                    // H.264 output is 8-bit 4:2:0. QSV VPP keeps the conversion
                    // on the device for 8/10-bit hardware-decoded input.
                    arguments.AddRange(["-vf", "vpp_qsv=format=nv12"]);
                }
                arguments.AddRange([
                    "-c:v", "h264_qsv",
                    "-preset", "fast",
                    "-profile:v", encoding.Profile,
                    "-level:v", encoding.Level,
                    "-b:v", FormatKbps(averageRate),
                    "-maxrate:v", FormatKbps(encoding.MaxRateBitsPerSecond),
                    "-bufsize:v", FormatKbps(encoding.BufferBitsPerSecond),
                    "-scenario", "livestreaming",
                    "-look_ahead", "0",
                    "-async_depth", "4",
                    "-forced_idr", "1",
                    "-repeat_pps", "1",
                    "-bf", EncoderBFrames.ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "nv12"
                ]);
            }
            else
            {
                arguments.AddRange([
                    "-c:v", "libx264",
                    "-preset", "fast",
                    "-profile:v", encoding.Profile,
                    "-level:v", encoding.Level,
                    "-crf", encoding.Crf.ToString(CultureInfo.InvariantCulture),
                    "-maxrate", FormatKbps(encoding.MaxRateBitsPerSecond),
                    "-bufsize", FormatKbps(encoding.BufferBitsPerSecond),
                    "-bf", EncoderBFrames.ToString(CultureInfo.InvariantCulture),
                    "-threads:v", EncoderThreads.ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "yuv420p"
                ]);
            }
            var encoderLabel = encoderMode switch
            {
                VideoEncoderMode.Nvidia => "NVENC",
                VideoEncoderMode.QuickSyncHardware => "Intel Quick Sync hardware decode/encode",
                VideoEncoderMode.QuickSync => "Intel Quick Sync encode",
                _ => $"libx264/{EncoderThreads} threads"
            };
            _logger.LogDebug(
                "Re-encoding segment with {Encoder} as H.264 {Profile}@{Level}, quality {Quality}, maxrate {MaxRateKbps} kbps",
                encoderLabel,
                encoding.Profile,
                encoding.Level,
                encoding.Crf,
                encoding.MaxRateBitsPerSecond / 1000);
        }
        if (tracks.Audio is not null)
        {
            if (IsBrowserCompatibleAudio(tracks.Audio))
            {
                arguments.AddRange(["-c:a", "copy"]);
            }
            else
            {
                // Keep the fallback inside the browser-safe AAC-LC stereo
                // profile. Multichannel AAC output is not consistently
                // decodable by MediaSource across browsers.
                arguments.AddRange(["-c:a", "aac", "-profile:a", "aac_low", "-ac", "2", "-b:a", "192k"]);
            }
        }
        else
        {
            arguments.Add("-an");
        }
        if (tracks.Subtitle is not null)
        {
            arguments.AddRange(["-c:s", "mov_text"]);
        }
        else
        {
            arguments.Add("-sn");
        }
        // Do not carry Matroska/data attachments into the browser fragment.
        arguments.Add("-dn");

        arguments.AddRange([
            "-map_metadata", "-1",
            "-map_chapters", "-1",
            "-avoid_negative_ts", "make_zero",
            "-movflags", "frag_keyframe+empty_moov+default_base_moof+separate_moof",
            "-f", "mp4",
            "pipe:1"
        ]);

        return new Process
        {
            StartInfo = BuildStartInfo(_options.FfmpegPath, arguments, redirectStandardOutput: true),
            EnableRaisingEvents = true
        };
    }

    private async Task<bool> IsNvidiaEncoderAvailableAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _nvidiaEncoderState);
        if (current >= 0) return current == 1;

        await _nvidiaProbeLock.WaitAsync(cancellationToken);
        try
        {
            current = Volatile.Read(ref _nvidiaEncoderState);
            if (current >= 0) return current == 1;

            using var process = new Process
            {
                StartInfo = BuildStartInfo(_options.FfmpegPath, new[]
                {
                    "-hide_banner",
                    "-loglevel", "error",
                    "-nostdin",
                    "-f", "lavfi",
                    "-i", "color=c=black:s=256x256:r=1",
                    "-frames:v", "1",
                    "-an",
                    "-c:v", "h264_nvenc",
                    "-preset", "p4",
                    "-f", "null",
                    "NUL"
                }, redirectStandardOutput: false),
                EnableRaisingEvents = true
            };
            try
            {
                StartProcess(process);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var error = await errorTask;
                var available = process.ExitCode == 0;
                Volatile.Write(ref _nvidiaEncoderState, available ? 1 : 0);
                if (available)
                {
                    _logger.LogInformation("NVIDIA NVENC is available and will be preferred for video transcoding.");
                }
                else
                {
                    _logger.LogInformation("NVIDIA NVENC probe failed; Intel Quick Sync will be tried next: {Error}", error.Trim());
                }
                return available;
            }
            finally
            {
                StopProcess(process);
            }
        }
        finally
        {
            _nvidiaProbeLock.Release();
        }
    }

    private async Task<VideoEncoderMode> SelectPreferredEncoderAsync(
        SelectedTracks tracks,
        CancellationToken cancellationToken)
    {
        if (await IsNvidiaEncoderAvailableAsync(cancellationToken)) return VideoEncoderMode.Nvidia;
        return await SelectQuickSyncOrSoftwareAsync(tracks, cancellationToken);
    }

    private async Task<VideoEncoderMode> SelectQuickSyncOrSoftwareAsync(
        SelectedTracks tracks,
        CancellationToken cancellationToken)
    {
        if (!await IsQuickSyncEncoderAvailableAsync(cancellationToken)) return VideoEncoderMode.Software;
        return _quickSyncDecodeFailures.ContainsKey(GetProbeKey(tracks.SourcePath))
            ? VideoEncoderMode.QuickSync
            : VideoEncoderMode.QuickSyncHardware;
    }

    private async Task<bool> IsQuickSyncEncoderAvailableAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _quickSyncEncoderState);
        if (current >= 0) return current == 1;

        await _quickSyncProbeLock.WaitAsync(cancellationToken);
        try
        {
            current = Volatile.Read(ref _quickSyncEncoderState);
            if (current >= 0) return current == 1;

            using var process = new Process
            {
                StartInfo = BuildStartInfo(_options.FfmpegPath, new[]
                {
                    "-hide_banner",
                    "-loglevel", "error",
                    "-nostdin",
                    "-f", "lavfi",
                    "-i", "color=c=black:s=256x256:r=1",
                    "-frames:v", "1",
                    "-an",
                    "-c:v", "h264_qsv",
                    "-preset", "fast",
                    "-bf", EncoderBFrames.ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "nv12",
                    "-f", "null",
                    "NUL"
                }, redirectStandardOutput: false),
                EnableRaisingEvents = true
            };
            try
            {
                StartProcess(process);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var error = await errorTask;
                var available = process.ExitCode == 0;
                Volatile.Write(ref _quickSyncEncoderState, available ? 1 : 0);
                if (available)
                {
                    _logger.LogInformation("Intel Quick Sync is available and will be preferred over libx264 for video transcoding.");
                }
                else
                {
                    _logger.LogInformation("Intel Quick Sync probe failed; libx264 will be used: {Error}", error.Trim());
                }
                return available;
            }
            finally
            {
                StopProcess(process);
            }
        }
        finally
        {
            _quickSyncProbeLock.Release();
        }
    }

    private static bool NeedsProcessing(SelectedTracks tracks) =>
        !IsBrowserCompatibleVideo(tracks.Video)
        || tracks.Audio is not null && !IsBrowserCompatibleAudio(tracks.Audio)
        || tracks.Subtitle is not null;

    private static SegmentEncodingInfo DescribeSegment(SelectedTracks tracks, VideoEncoderMode encoderMode, bool allowHevc)
    {
        if (IsBrowserCompatibleVideo(tracks.Video, allowHevc))
        {
            var hevc = IsHevcVideo(tracks.Video);
            return new SegmentEncodingInfo(
                hevc ? "copy-hevc" : "copy-h264",
                hevc ? "HEVC/hvc1" : "H.264/avc1",
                tracks.Video.Profile,
                tracks.Video.Level?.ToString(CultureInfo.InvariantCulture),
                null,
                null,
                null);
        }

        var encoding = SelectVideoEncoding(tracks);
        var hardwareEncoder = encoderMode != VideoEncoderMode.Software;
        long? targetRate = hardwareEncoder ? RoundBitRate(encoding.MaxRateBitsPerSecond * 4 / 5) : null;
        var mode = encoderMode switch
        {
            VideoEncoderMode.Nvidia => "nvenc",
            VideoEncoderMode.QuickSyncHardware => "qsv-hw",
            VideoEncoderMode.QuickSync => "qsv",
            _ => "libx264"
        };
        var quality = encoderMode switch
        {
            VideoEncoderMode.Nvidia => $"CQ {encoding.Crf}",
            VideoEncoderMode.QuickSyncHardware or VideoEncoderMode.QuickSync => "VBR adaptive",
            _ => $"CRF {encoding.Crf}"
        };
        return new SegmentEncodingInfo(
            mode,
            "H.264/avc1",
            encoding.Profile,
            encoding.Level,
            quality,
            targetRate,
            encoding.MaxRateBitsPerSecond);
    }

    private static VideoEncodingPlan SelectVideoEncoding(SelectedTracks tracks)
    {
        var width = Math.Max(1, tracks.Video.Width ?? 1920);
        var height = Math.Max(1, tracks.Video.Height ?? 1080);
        var framesPerSecond = ParseFrameRate(tracks.Video.AverageFrameRate)
            ?? ParseFrameRate(tracks.Video.RealFrameRate)
            ?? 30;
        framesPerSecond = Math.Clamp(framesPerSecond, 1, 120);

        var pixels = (long)width * height;
        var frameRateScale = Math.Clamp(Math.Sqrt(framesPerSecond / 30), 0.75, 1.75);
        var tierCeiling = pixels switch
        {
            <= 720L * 576 => 6_000_000,
            <= 1280L * 720 => 12_000_000,
            <= 1920L * 1080 => 25_000_000,
            <= 2560L * 1440 => 40_000_000,
            <= 3840L * 2160 => 70_000_000,
            _ => 80_000_000
        };
        var qualityCeiling = (long)Math.Round(tierCeiling * frameRateScale);

        var sourceBitRate = ParsePositiveLong(tracks.Video.BitRate)
            ?? ParsePositiveLong(tracks.FormatBitRate);
        long desiredRate;
        if (sourceBitRate is long knownRate)
        {
            var codecFactor = tracks.Video.CodecName?.ToLowerInvariant() switch
            {
                "av1" => 2.5,
                "hevc" or "h265" or "vp9" => 2.2,
                "vp8" => 1.5,
                "h264" => 1.15,
                _ => 1.35
            };
            var highBitDepthFactor = tracks.Video.PixelFormat?.Contains("10", StringComparison.Ordinal) == true
                || tracks.Video.PixelFormat?.Contains("12", StringComparison.Ordinal) == true
                ? 1.15
                : 1;
            desiredRate = (long)Math.Round(knownRate * codecFactor * highBitDepthFactor);
        }
        else
        {
            // A conservative H.264 bits-per-pixel fallback for containers that
            // do not expose stream/format bitrate through ffprobe.
            desiredRate = (long)Math.Round(pixels * framesPerSecond * 0.16);
        }

        var maxRate = RoundBitRate(Math.Clamp(desiredRate, 600_000, qualityCeiling));
        var qualityRatio = (double)maxRate / qualityCeiling;
        var crf = qualityRatio switch
        {
            >= 0.75 => 16,
            >= 0.45 => 17,
            >= 0.25 => 18,
            _ => 20
        };
        var profile = pixels <= 720L * 576 && maxRate <= 5_000_000 ? "main" : "high";
        var level = SelectH264Level(width, height, framesPerSecond);
        var bufferSize = Math.Min(160_000_000L, maxRate * 2);
        return new VideoEncodingPlan(profile, level, crf, maxRate, bufferSize);
    }

    private static double EstimateEncoderPresentationLead(SelectedTracks tracks, string encoderMode)
    {
        var framesPerSecond = ParseFrameRate(tracks.Video.AverageFrameRate)
            ?? ParseFrameRate(tracks.Video.RealFrameRate)
            ?? 30;
        var delayFrames = encoderMode switch
        {
            "nvenc" => NvidiaPresentationDelayFrames,
            "qsv" or "qsv-hw" => QuickSyncPresentationDelayFrames,
            _ => X264PresentationDelayFrames
        };
        return delayFrames / Math.Clamp(framesPerSecond, 1, 120);
    }

    private static string SelectH264Level(int width, int height, double framesPerSecond)
    {
        var pixels = (long)width * height;
        if (pixels <= 720L * 576 && framesPerSecond <= 30) return "3.1";
        if (pixels <= 1280L * 720) return framesPerSecond <= 30 ? "3.1" : "3.2";
        if (pixels <= 1920L * 1080) return framesPerSecond <= 30 ? "4.1" : "4.2";
        if (pixels <= 2560L * 1440) return "5.0";
        return framesPerSecond <= 30 ? "5.1" : "5.2";
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/', 2);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)) return null;
        var denominator = 1d;
        if (parts.Length == 2
            && (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator)
                || denominator == 0)) return null;
        var result = numerator / denominator;
        return double.IsFinite(result) && result > 0 ? result : null;
    }

    private static long? ParsePositiveLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static long RoundBitRate(long value) => Math.Max(100_000, (long)Math.Round(value / 100_000d) * 100_000);

    private static string FormatKbps(long bitsPerSecond) =>
        $"{Math.Max(1, bitsPerSecond / 1000).ToString(CultureInfo.InvariantCulture)}k";

    private async Task<SeekPlan> ResolveSeekPlanAsync(
        SelectedTracks tracks,
        double startSeconds,
        CancellationToken cancellationToken)
    {
        var keyframe = await FindKeyframeBeforeAsync(tracks.SourcePath, startSeconds, cancellationToken);
        if (keyframe is not double keyframeSeconds
            || keyframeSeconds < 0
            || keyframeSeconds > startSeconds)
        {
            // The input seek is still fast if the optional keyframe probe is
            // unavailable; this fallback may include a small keyframe pre-roll.
            return new SeekPlan(startSeconds, 0);
        }

        return new SeekPlan(keyframeSeconds, Math.Max(0, startSeconds - keyframeSeconds));
    }

    private async Task<CopySegmentPlan> ResolveCopySegmentPlanAsync(
        string path,
        double requestedStart,
        double requestedDuration,
        CancellationToken cancellationToken)
    {
        var keyframes = (await GetKeyframesAroundAsync(path, requestedStart, cancellationToken))
            .OrderBy(value => value.PresentationSeconds)
            .GroupBy(value => value.PresentationSeconds)
            .Select(group => group.First())
            .ToArray();
        var startPoint = keyframes.LastOrDefault(value => value.PresentationSeconds <= requestedStart + 0.002);
        var start = startPoint?.PresentationSeconds ?? 0;
        var decodeStart = startPoint?.DecodeSeconds ?? start;
        if (requestedStart > 0.05 && startPoint is null)
        {
            start = requestedStart;
            decodeStart = requestedStart;
        }

        var desiredEnd = Math.Max(requestedStart + requestedDuration, start + 0.05);
        var nextPoint = keyframes.FirstOrDefault(value => value.PresentationSeconds >= desiredEnd - 0.002);
        var next = nextPoint?.PresentationSeconds ?? 0;
        var nextDecode = nextPoint?.DecodeSeconds ?? next;
        if (next <= start + 0.05)
        {
            var followingPoint = (await GetKeyframesAroundAsync(path, desiredEnd, cancellationToken))
                .Where(value => value.PresentationSeconds >= desiredEnd - 0.002)
                .OrderBy(value => value.PresentationSeconds)
                .FirstOrDefault();
            var following = followingPoint?.PresentationSeconds ?? 0;
            next = following > start + 0.05 ? following : desiredEnd;
            nextDecode = following > start + 0.05 ? followingPoint!.DecodeSeconds : desiredEnd;
        }

        var inputStart = keyframes
            .Where(value => value.PresentationSeconds < start - 0.002)
            .Select(value => value.PresentationSeconds)
            .DefaultIfEmpty(start)
            .Last();
        // FFmpeg applies -t on decode timestamps. End immediately before the
        // next keyframe's DTS so that keyframe belongs only to the next fragment
        // without dropping the B-frames that precede it in presentation order.
        return new CopySegmentPlan(
            inputStart,
            start,
            decodeStart,
            Math.Max(0.05, nextDecode - decodeStart - CopySegmentEndGuardSeconds),
            next);
    }

    private async Task<double?> FindKeyframeBeforeAsync(
        string path,
        double startSeconds,
        CancellationToken cancellationToken)
    {
        if (startSeconds <= 0)
        {
            return 0;
        }

        IReadOnlyList<KeyframePoint> keyframes;
        try
        {
            keyframes = await GetKeyframesAroundAsync(path, startSeconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Keyframe probe failed for {SourcePath}; using fast input seek fallback", path);
            return null;
        }

        double? best = null;
        foreach (var keyframe in keyframes)
        {
            if (keyframe.PresentationSeconds <= startSeconds + 0.001
                && (best is null || keyframe.PresentationSeconds > best.Value))
            {
                best = keyframe.PresentationSeconds;
            }
        }

        return best;
    }

    private async Task<IReadOnlyList<KeyframePoint>> GetKeyframesAroundAsync(
        string path,
        double anchorSeconds,
        CancellationToken cancellationToken)
    {
        var bucketStart = Math.Floor(anchorSeconds / KeyframeBucketSeconds) * KeyframeBucketSeconds;
        var intervalStart = Math.Max(0, bucketStart - KeyframeLookBehindSeconds);
        var cacheKey = string.Join('|',
            GetProbeKey(path),
            FormatSeconds(intervalStart),
            FormatSeconds(KeyframeReadDurationSeconds));
        if (_keyframeCache.TryGetValue(cacheKey, out var cached)) return cached;
        var keyframes = await ProbeKeyframesAsync(path, intervalStart, cancellationToken);
        if (keyframes.Count > 0) _keyframeCache.TryAdd(cacheKey, keyframes);
        return keyframes;
    }

    private async Task<IReadOnlyList<KeyframePoint>> ProbeKeyframesAsync(
        string path,
        double intervalStart,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = BuildStartInfo(_options.FfprobePath, new[]
            {
                "-v", "error",
                "-select_streams", "v:0",
                "-show_packets",
                "-show_entries", "packet=pts_time,dts_time,flags",
                "-of", "csv=p=0",
                "-read_intervals", $"{FormatSeconds(intervalStart)}%+{FormatSeconds(KeyframeReadDurationSeconds)}",
                path
            }, redirectStandardOutput: true),
            EnableRaisingEvents = true
        };

        try
        {
            StartProcess(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "FFprobe is unavailable while locating a keyframe for {SourcePath}", path);
            return Array.Empty<KeyframePoint>();
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outputTask, errorTask);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                _logger.LogDebug("FFprobe keyframe query exited with code {ExitCode}: {Error}", process.ExitCode, error.Trim());
                return Array.Empty<KeyframePoint>();
            }

            return ParseKeyframeTimes(output);
        }
        finally
        {
            StopProcess(process);
        }
    }

    private static IReadOnlyList<KeyframePoint> ParseKeyframeTimes(string output)
    {
        var values = new List<KeyframePoint>();
        foreach (var line in output.Split([ '\r', '\n' ], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(',');
            if (fields.Length < 3 || !fields[2].Contains('K', StringComparison.Ordinal)) continue;
            if (double.TryParse(fields[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var presentation)
                && double.IsFinite(presentation)
                && presentation >= 0)
            {
                var decode = double.TryParse(fields[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDecode)
                    && double.IsFinite(parsedDecode)
                    && parsedDecode >= 0
                        ? parsedDecode
                        : presentation;
                values.Add(new KeyframePoint(presentation, Math.Min(presentation, decode)));
            }
        }

        return values;
    }

    private async Task<ProbeDocument> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var key = GetProbeKey(path);
        if (_probeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var process = new Process
        {
            StartInfo = BuildStartInfo(_options.FfprobePath, new[]
            {
                "-v", "error",
                "-show_streams",
                "-show_format",
                "-of", "json",
                path
            }, redirectStandardOutput: true),
            EnableRaisingEvents = true
        };

        try
        {
            StartProcess(process);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new MediaPlaybackException("Video playback tools are unavailable on this server.", 503, ex);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outputTask, errorTask);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFprobe exited with code {ExitCode}: {Error}", process.ExitCode, error.Trim());
                throw new MediaPlaybackException("This video file could not be inspected.", 415);
            }

            var document = JsonSerializer.Deserialize<ProbeDocument>(output, JsonOptions)
                ?? throw new MediaPlaybackException("This video file could not be inspected.", 415);
            _probeCache[key] = document;
            return document;
        }
        catch (JsonException ex)
        {
            throw new MediaPlaybackException("This video file could not be inspected.", 415, ex);
        }
        finally
        {
            StopProcess(process);
        }
    }

    private static async Task<byte[]> ReadOutputAsync(
        Stream output,
        CancellationToken cancellationToken,
        int maxBytes = MaxSegmentBytes)
    {
        await using var bufferStream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await output.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (bufferStream.Length + read > maxBytes)
                {
                    throw new MediaPlaybackException("The requested media output is too large.", 413);
                }
                await bufferStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return bufferStream.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        bool redirectStandardOutput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static void StartProcess(Process process)
    {
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The media process did not start.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new MediaPlaybackException("Video playback tools are unavailable on this server.", 503, ex);
        }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The worker is already shutting down.
        }
        process.Dispose();
    }

    private static string GetProbeKey(string path)
    {
        var source = new FileInfo(path);
        if (!source.Exists)
        {
            throw new MediaPlaybackException("Video file not found.", 404);
        }
        return string.Join('|',
            source.FullName,
            source.Length.ToString(CultureInfo.InvariantCulture),
            source.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    private static MediaTrackDto ToTrack(ProbeStream stream) => new(
        stream.Index,
        stream.CodecType ?? "unknown",
        stream.CodecName ?? "unknown",
        stream.Profile,
        stream.Level,
        stream.Width,
        stream.Height,
        ParsePositiveLong(stream.BitRate),
        ParseFrameRate(stream.AverageFrameRate) ?? ParseFrameRate(stream.RealFrameRate),
        stream.PixelFormat,
        GetTag(stream, "language"),
        GetTag(stream, "title"),
        IsDefault(stream),
        !IsType(stream, "subtitle") || IsSupportedSubtitle(stream));

    private static bool IsType(ProbeStream stream, string type) =>
        string.Equals(stream.CodecType, type, StringComparison.OrdinalIgnoreCase);

    private static bool IsDefault(ProbeStream stream) => stream.Disposition?.Default == 1;

    private static bool IsBrowserCompatibleVideo(ProbeStream stream) =>
        string.Equals(stream.CodecName, "h264", StringComparison.OrdinalIgnoreCase)
        && stream.PixelFormat is "yuv420p" or "yuvj420p";

    private static bool IsBrowserCompatibleVideo(ProbeStream stream, bool allowHevc) =>
        IsBrowserCompatibleVideo(stream)
        || allowHevc
            && IsHevcVideo(stream)
            && stream.PixelFormat is "yuv420p" or "yuvj420p" or "yuv420p10le";

    private static bool IsHevcVideo(ProbeStream stream) =>
        stream.CodecName is not null
        && (string.Equals(stream.CodecName, "hevc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stream.CodecName, "h265", StringComparison.OrdinalIgnoreCase));

    private static bool IsBrowserCompatibleAudio(ProbeStream stream) =>
        string.Equals(stream.CodecName, "aac", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedSubtitle(ProbeStream stream) =>
        stream.CodecName is not null && TextSubtitleCodecs.Contains(stream.CodecName);

    private static string? GetTag(ProbeStream stream, string key) =>
        stream.Tags is not null && stream.Tags.TryGetValue(key, out var value) ? value : null;

    private static double? ParseDuration(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : null;

    private static string FormatSeconds(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record SelectedTracks(
        string SourcePath,
        ProbeStream Video,
        ProbeStream? Audio,
        ProbeStream? Subtitle,
        string? FormatBitRate);

    private enum VideoEncoderMode
    {
        Software,
        Nvidia,
        QuickSyncHardware,
        QuickSync
    }

    private sealed record VideoEncodingPlan(
        string Profile,
        string Level,
        int Crf,
        long MaxRateBitsPerSecond,
        long BufferBitsPerSecond);

    private sealed record SegmentEncodingInfo(
        string Mode,
        string OutputCodec,
        string? Profile,
        string? Level,
        string? Quality,
        long? TargetBitRate,
        long? MaxBitRate);

    private sealed record FfmpegResult(int ExitCode, byte[] Output, string Error, SegmentEncodingInfo Info)
    {
        public bool Succeeded => ExitCode == 0 && Output.Length > 0;
    }

    private sealed record SeekPlan(double InputSeconds, double OutputSeconds);

    private sealed record KeyframePoint(double PresentationSeconds, double DecodeSeconds);

    private sealed record CopySegmentPlan(
        double InputStartSeconds,
        double StartSeconds,
        double DecodeStartSeconds,
        double DurationSeconds,
        double NextStartSeconds);

    private sealed class SubtitleProgressState(string id)
    {
        private readonly object _gate = new();
        private double? _totalSeconds;
        private double _processedSeconds;
        private bool _complete;
        private bool _failed;
        private bool _cancelled;

        private DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public bool IsStale(DateTimeOffset cutoff)
        {
            lock (_gate) return UpdatedAt < cutoff;
        }

        public void SetTotalSeconds(double? totalSeconds)
        {
            lock (_gate)
            {
                _totalSeconds = totalSeconds is > 0 ? totalSeconds : null;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void ReportSeconds(double processedSeconds)
        {
            lock (_gate)
            {
                _processedSeconds = Math.Max(_processedSeconds, processedSeconds);
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Complete() => SetTerminal(complete: true, failed: false, cancelled: false);
        public void Fail() => SetTerminal(complete: false, failed: true, cancelled: false);
        public void Cancel() => SetTerminal(complete: false, failed: false, cancelled: true);

        public SubtitleProgressDto Snapshot()
        {
            lock (_gate)
            {
                double? percent = _complete
                    ? 100d
                    : _totalSeconds is > 0
                        ? Math.Clamp(_processedSeconds / _totalSeconds.Value * 100d, 0, 99d)
                        : null;
                return new SubtitleProgressDto(
                    id,
                    percent,
                    _processedSeconds,
                    _totalSeconds,
                    _complete,
                    _failed,
                    _cancelled);
            }
        }

        private void SetTerminal(bool complete, bool failed, bool cancelled)
        {
            lock (_gate)
            {
                _complete = complete;
                _failed = failed;
                _cancelled = cancelled;
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private sealed class ProbeDocument
    {
        [JsonPropertyName("streams")]
        public List<ProbeStream> Streams { get; set; } = [];

        [JsonPropertyName("format")]
        public ProbeFormat? Format { get; set; }
    }

    private sealed class ProbeFormat
    {
        [JsonPropertyName("duration")]
        public string? Duration { get; set; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; set; }
    }

    private sealed class ProbeStream
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; }

        [JsonPropertyName("pix_fmt")]
        public string? PixelFormat { get; set; }

        [JsonPropertyName("profile")]
        public string? Profile { get; set; }

        [JsonPropertyName("level")]
        public int? Level { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; set; }

        [JsonPropertyName("avg_frame_rate")]
        public string? AverageFrameRate { get; set; }

        [JsonPropertyName("r_frame_rate")]
        public string? RealFrameRate { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; }

        [JsonPropertyName("disposition")]
        public ProbeDisposition? Disposition { get; set; }

    }

    private sealed class ProbeDisposition
    {
        [JsonPropertyName("default")]
        public int Default { get; set; }
    }
}

public sealed record SubtitleProgressDto(
    string Id,
    double? Percent,
    double ProcessedSeconds,
    double? TotalSeconds,
    bool Complete,
    bool Failed,
    bool Cancelled);
