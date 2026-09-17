using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;

if (args.Length < 1) { Console.WriteLine("Usage: ThumbnailBenchmark <image folder> [count=24] [settings.json]"); return; }
var folder = Path.GetFullPath(args[0]);
var count = args.Length > 1 ? Math.Clamp(int.Parse(args[1]), 1, 64) : 24;
int width = 480, height = 360, quality = 78;
if (args.Length > 2)
{
    using var settings = JsonDocument.Parse(File.ReadAllText(args[2]));
    var g = settings.RootElement.GetProperty("Gallery");
    width = g.GetProperty("ThumbnailWidth").GetInt32(); height = g.GetProperty("ThumbnailHeight").GetInt32(); quality = g.GetProperty("ThumbnailQuality").GetInt32();
}
var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff" };
var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint })
    .Where(p => extensions.Contains(Path.GetExtension(p)) && new FileInfo(p).Length <= 128L * 1024 * 1024).Take(count).ToArray();
if (files.Length == 0) { Console.WriteLine("No supported images found."); return; }
// Reuse precisely the previous NAS sample when a report is supplied.
if (args.Length > 3)
{
    using var previous = JsonDocument.Parse(File.ReadAllText(args[3]));
    files = previous.RootElement.GetProperty("runs")[0].GetProperty("rows").EnumerateArray()
        .Select(r => r.GetProperty("File").GetString()!).Distinct().Take(count).ToArray();
}
var output = Path.Combine(AppContext.BaseDirectory, "results-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(output);
Console.WriteLine($"Host={Environment.MachineName}, CPUs={Environment.ProcessorCount}, images={files.Length}, output={output}");
Console.WriteLine("Close gallery tabs and wait for CPU to settle first. Read timing includes Windows file cache. Later runs are warmer.");
// Warm up JIT/codec without pre-reading the sample set.
using (var warm = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(32, 32))
using (var buffer = new MemoryStream()) { warm.Mutate(x => x.Resize(16, 16)); warm.SaveAsWebp(buffer); }
var runs = new List<object>();
Image Decode(byte[] bytes, bool reduced)
{
    if (!reduced || bytes.Length < 2 || bytes[0] != 255 || bytes[1] != 216) return Image.Load(bytes);
    using var stream = new MemoryStream(bytes, writable: false);
    // Square bound avoids under-decoding EXIF-rotated portrait images.
    return JpegDecoder.Instance.Decode(new JpegDecoderOptions {
        GeneralOptions = new DecoderOptions { TargetSize = new Size(Math.Max(width, height), Math.Max(width, height)) },
        ResizeMode = JpegDecoderResizeMode.IdctOnly
    }, stream);
}
// Warm both real decode paths and encoders; exclude this from measurements.
var warmBytes = await File.ReadAllBytesAsync(files[0]);
foreach (var reduced in new[] { false, true })
{
    using var warmImage = Decode(warmBytes, reduced);
    warmImage.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 }));
    using var warmOutput = new MemoryStream(); warmImage.SaveAsWebp(warmOutput, new WebpEncoder { Quality = quality });
}
foreach (var trial in new[] { (Round: 1, Reduced: false), (Round: 1, Reduced: true), (Round: 2, Reduced: true), (Round: 2, Reduced: false) })
{
    const int workers = 4;
    var method = trial.Reduced ? "jpeg-idct" : "full-decode";
    var rows = new ConcurrentBag<Row>();
    var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
    var elapsed = Stopwatch.StartNew();
    await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = workers }, async (file, ct) =>
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var bytes = await File.ReadAllBytesAsync(file, ct); var read = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            using var image = Decode(bytes, trial.Reduced); var decode = sw.Elapsed.TotalMilliseconds;
            var dimensions = $"{image.Width}x{image.Height}";
            sw.Restart();
            image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 }));
            var resize = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            using var encoded = new MemoryStream();
            await image.SaveAsWebpAsync(encoded, new WebpEncoder { Quality = quality }, ct);
            var encode = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var target = Path.Combine(output, $"{method}-{Array.IndexOf(files, file):D3}.webp");
            await File.WriteAllBytesAsync(target, encoded.ToArray(), ct);
            var write = sw.Elapsed.TotalMilliseconds;
            // Retain paired thumbnails for visual comparison outside timing.
            rows.Add(new(file, dimensions, bytes.Length, read, decode, resize, encode, write, null));
        }
        catch (Exception ex) { rows.Add(new(file, "", 0, 0, 0, 0, 0, 0, ex.GetType().Name + ": " + ex.Message)); }
    });
    elapsed.Stop();
    var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds;
    var good = rows.Where(r => r.Error == null).ToArray();
    Console.WriteLine($"{method} round={trial.Round}, workers={workers}: {good.Length}/{files.Length} OK, {elapsed.Elapsed.TotalSeconds:F2}s, {good.Length / elapsed.Elapsed.TotalSeconds:F2} images/s, process CPU={100 * cpuMs / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount:F1}%");
    if (good.Length > 0) Console.WriteLine($"Mean ms/image: read={good.Average(r=>r.ReadMs):F1}, decode={good.Average(r=>r.DecodeMs):F1}, resize/orient={good.Average(r=>r.ResizeMs):F1}, WebP={good.Average(r=>r.EncodeMs):F1}, write={good.Average(r=>r.WriteMs):F1}");
    runs.Add(new { method, round = trial.Round, workers, wallSeconds = elapsed.Elapsed.TotalSeconds, cpuMs, rows });
    await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { host = Environment.MachineName, processors = Environment.ProcessorCount, width, height, quality, note = "Standalone decomposed benchmark; memory buffering differs from production streaming; Windows cache not flushed; sample selection is first matching files, not random; not IIS queue/request timing.", runs }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine("Done. Report: " + Path.Combine(output, "report.json"));
record Row(string File, string Dimensions, long Bytes, double ReadMs, double DecodeMs, double ResizeMs, double EncodeMs, double WriteMs, string? Error);
