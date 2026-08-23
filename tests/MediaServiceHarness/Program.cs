using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebGallery.Models;
using WebGallery.Services;

if (args.Length != 3)
    throw new InvalidOperationException("Usage: MediaServiceHarness <video> <ffmpeg> <ffprobe>");

var service = new MediaService(Options.Create(new GalleryOptions
{
    FfmpegPath = args[1],
    FfprobePath = args[2]
}), NullLogger<MediaService>.Instance);

var metadata = await service.GetMetadataAsync(args[0], CancellationToken.None);
if (metadata.Video.Count == 0 || metadata.Duration is null or <= 0)
    throw new InvalidOperationException("Media metadata did not contain a playable video and duration.");

var segment = await service.GetSegmentAsync(args[0], null, null, 0, 2, CancellationToken.None);
Console.WriteLine($"TRACE output={segment.OutputCodec} mode={segment.Mode} bytes={segment.Bytes.Length}");
if (segment.Bytes.Length < 1024 || !segment.OutputCodec.Contains("264", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The generated browser segment was empty or not H.264.");

Console.WriteLine($"PASS duration={metadata.Duration:0.###}s video={metadata.Video[0].Codec} mode={segment.Mode} segment={segment.Bytes.Length} bytes next={segment.NextStart:0.###}s");
