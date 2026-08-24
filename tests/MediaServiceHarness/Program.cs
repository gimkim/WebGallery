using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebGallery.Models;
using WebGallery.Services;

if (args.Length is < 3 or > 5)
    throw new InvalidOperationException("Usage: MediaServiceHarness <video> <ffmpeg> <ffprobe> [start-seconds] [segment-count]");

var startSeconds = args.Length >= 4
    ? double.Parse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture)
    : 0;
var segmentCount = args.Length >= 5
    ? int.Parse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture)
    : 1;
if (startSeconds < 0 || segmentCount is < 1 or > 20)
    throw new InvalidOperationException("Start seconds must be non-negative and segment count must be between 1 and 20.");

var service = new MediaService(Options.Create(new GalleryOptions
{
    FfmpegPath = args[1],
    FfprobePath = args[2]
}), NullLogger<MediaService>.Instance);

var metadata = await service.GetMetadataAsync(args[0], CancellationToken.None);
if (metadata.Video.Count == 0 || metadata.Duration is null or <= 0)
    throw new InvalidOperationException("Media metadata did not contain a playable video and duration.");

var cursor = startSeconds;
for (var index = 0; index < segmentCount; index++)
{
    var segment = await service.GetSegmentAsync(args[0], null, null, cursor, 8, CancellationToken.None);
    var hasVideo = await SegmentContainsVideoAsync(args[2], segment.Bytes);
    Console.WriteLine(
        $"TRACE index={index + 1} request={cursor:0.######} source={segment.SourceStart:0.######} " +
        $"next={segment.NextStart:0.######} output={segment.OutputCodec} mode={segment.Mode} " +
        $"bytes={segment.Bytes.Length} video={hasVideo}");
    if (segment.Bytes.Length < 1024 || !segment.OutputCodec.Contains("264", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Segment {index + 1} was empty or not H.264.");
    if (!hasVideo)
        throw new InvalidOperationException($"Segment {index + 1} did not contain a video stream.");
    if (segment.NextStart <= cursor + 0.001)
        throw new InvalidOperationException($"Segment {index + 1} did not advance the source cursor.");
    cursor = segment.NextStart;
}

Console.WriteLine($"PASS duration={metadata.Duration:0.###}s video={metadata.Video[0].Codec} segments={segmentCount} final={cursor:0.######}s");

static async Task<bool> SegmentContainsVideoAsync(string ffprobePath, byte[] segment)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.StartInfo.ArgumentList.Add("-v");
    process.StartInfo.ArgumentList.Add("error");
    process.StartInfo.ArgumentList.Add("-show_entries");
    process.StartInfo.ArgumentList.Add("stream=codec_type");
    process.StartInfo.ArgumentList.Add("-of");
    process.StartInfo.ArgumentList.Add("csv=p=0");
    process.StartInfo.ArgumentList.Add("-i");
    process.StartInfo.ArgumentList.Add("pipe:0");

    if (!process.Start())
        throw new InvalidOperationException("ffprobe could not be started.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    try
    {
        await process.StandardInput.BaseStream.WriteAsync(segment);
    }
    catch (IOException)
    {
        // ffprobe can close stdin as soon as it has identified the stream table,
        // before the complete fragmented MP4 has been written to its pipe.
        // Its exit code and output below still determine whether the probe passed.
    }
    process.StandardInput.Close();
    await process.WaitForExitAsync();
    var output = await outputTask;
    var error = await errorTask;
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"ffprobe rejected a generated segment: {error.Trim()}");

    return output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(value => value.Equals("video", StringComparison.OrdinalIgnoreCase));
}
