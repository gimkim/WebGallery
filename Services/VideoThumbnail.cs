using System.Diagnostics;

namespace WebGallery.Services;

public static class VideoThumbnail
{
    // Remote queues may have many slots; video decoding still runs on the source host.
    private static readonly SemaphoreSlim Slots = new(2, 2);
    public static async Task<Stream> OpenAsync(string path, string ffmpeg, string exifTool, CancellationToken token)
    {
        if (!FileSystemService.IsVideo(Path.GetExtension(path))) return await RawPreview.OpenAsync(path, exifTool, token);
        await Slots.WaitAsync(token);
        try {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            foreach (var seek in new[] { "1", "0" }) {
                var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "1",
                    "-filter_threads", "1", "-protocol_whitelist", "file,pipe", "-format_whitelist", "mov,matroska,webm,avi,mpegts,mpeg,flv,asf,ogg,rm",
                    "-ss", seek, "-i", Path.GetFullPath(path), "-map", "0:v:0", "-an", "-sn", "-dn",
                    "-frames:v", "1", "-vf", "scale=w='if(gte(dar,1),1500,max(2,trunc(1500*dar/2)*2))':h='if(gte(dar,1),max(2,trunc(1500/dar/2)*2),1500)',setsar=1",
                    "-c:v", "mjpeg", "-threads", "1", "-q:v", "2", "-f", "image2pipe", "pipe:1" }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new VideoThumbnailException("Cannot start FFmpeg.");
                using var kill = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
                var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
                var output = new MemoryStream();
                try {
                    var buffer = new byte[65536]; int count;
                    while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer, deadline.Token)) > 0) {
                        if (output.Length + count > 12 * 1024 * 1024) throw new VideoThumbnailException("Video preview exceeds safety limit.");
                        output.Write(buffer, 0, count);
                    }
                    await process.WaitForExitAsync(deadline.Token); await errors;
                    if (process.ExitCode == 0 && output.Length > 0) { output.Position = 0; return output; }
                    output.Dispose();
                } catch { output.Dispose(); throw; }
                finally {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    try { await errors; } catch { }
                }
            }
            throw new VideoThumbnailException("No video frame could be extracted.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (VideoThumbnailException) { throw; }
        catch (Exception ex) { throw new VideoThumbnailException("Video thumbnail extraction failed or timed out. Check FFmpeg and the source file.", ex); }
        finally { Slots.Release(); }
    }
}

public sealed class VideoThumbnailException(string message, Exception? inner = null) : Exception(message, inner);
