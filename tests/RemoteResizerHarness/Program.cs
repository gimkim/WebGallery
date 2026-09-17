using System.Net;
using Microsoft.Extensions.Options;
using WebGallery.Models;
using WebGallery.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Contains("--live")) {
    using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(@"C:\Users\tatsa\web\ThumbService\appsettings.json"));
    using var live = new RemoteResizer(Options.Create(new GalleryOptions {
        ResizerServiceUrl = "https://192.168.1.30/ThumbService",
        ResizerServiceApiKey = config.RootElement.GetProperty("ThumbService").GetProperty("ApiKey").GetString()!,
        ResizerServiceCertificateSha256 = "A6EC1572063548B9716544B01EF43075AA593CBF02BCEDF9DCDF84F05C1AC0AB"
    }));
    await live.StartAsync(default);
    using var deadline = new CancellationTokenSource(10000);
    try { while (!live.Status.StartsWith("Connected")) await Task.Delay(50,deadline.Token); }
    catch { throw new Exception(live.LastFailure); }
    var input = Path.GetTempFileName(); var output = Path.GetTempFileName();
    try {
        using var picture = new Image<Rgb24>(600,1200,new Rgb24(30,100,200));
        await picture.SaveAsJpegAsync(input);
        using var imageGate = new SemaphoreSlim(1);
        foreach (var fast in new[] {false,true}) {
            if (!await live.TryCreateAsync(input,output,480,480,fast,ThumbnailPriority.Visible,imageGate,()=>false,_=>Task.FromResult(true),default)) throw new Exception("Live resize failed");
            var header = await Image.IdentifyAsync(output);
            if (header.Width != 240 || header.Height != 480) throw new Exception("Aspect ratio mismatch");
            Console.WriteLine($"PASS live HTTPS pinned {(fast ? "fast" : "full")} WebP 240x480");
        }
    } finally { File.Delete(input); File.Delete(output); await live.StopAsync(default); }
    return;
}

var transport = new FakeService();
using var service = new RemoteResizer(Options.Create(new GalleryOptions { ResizerServiceUrl = "https://fixture/ThumbService", ResizerServiceRetrySeconds = 5 }), transport);
await service.StartAsync(default);
var oldSuffix = service.CacheSuffix;
service.UpdateSettings(91,7);
if (service.Quality != 91 || service.Workers != 7 || service.CacheSuffix == oldSuffix) throw new Exception("Runtime settings did not update");
if (service.QualityForVersion("contain-v1" + oldSuffix) != 78) throw new Exception("Old immutable URL lost its quality");
var newSuffix=service.CacheSuffix; service.UpdateSettings(91,1);
if (service.CacheSuffix != newSuffix || service.Workers != 1) throw new Exception("Worker changes must not invalidate caches");
Console.WriteLine("PASS live quality/worker changes and old URL quality preserved");
var fullVersion = "contain-v1" + service.CacheSuffix;
service.UpdateDecodeSettings("jpeg-idct",2);
if (service.DecodeMode != "jpeg-idct" || !service.FastForVersion(null) || service.FastForVersion(fullVersion)) throw new Exception("Remote decode snapshot failed");
if (fullVersion == "contain-v1" + service.CacheSuffix) throw new Exception("Remote decode must invalidate cache identity");
Console.WriteLine("PASS remote mode switches independently and preserves old mode URL");
using var gate = new SemaphoreSlim(1);
async Task Wait(Func<bool> ready) { using var timeout = new CancellationTokenSource(8000); while (!ready()) await Task.Delay(20, timeout.Token); }
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
await Wait(() => service.LastCheckedUtc != null);
for (var i = 0; i < 100; i++) Assert(!await service.TryCreateAsync("unused", "unused", 480, 480, false, ThumbnailPriority.Visible, gate, () => false, _ => Task.FromResult(true), default), i == 0 ? "offline skips remote" : "offline request " + i);
Assert(transport.Posts == 0 && transport.Checks == 1, "100 offline jobs cause no connection retries");
transport.Healthy = true;
service.RetryNow();
await Wait(() => service.Status.StartsWith("Connected"));
Assert(transport.Checks == 2, "manual retry reconnects immediately");
var source = Path.GetTempFileName();
try { Assert(!await service.TryCreateAsync(source, "unused", 480, 480, false, ThumbnailPriority.Visible, gate, () => false, _ => Task.FromResult(true), default), "failed resize falls back"); }
finally { File.Delete(source); }
Assert(transport.Posts == 1 && !service.Status.StartsWith("Connected"), "failed resize marks unavailable");
for (var i = 0; i < 100; i++) await service.TryCreateAsync("unused", "unused", 480, 480, false, ThumbnailPriority.Visible, gate, () => false, _ => Task.FromResult(true), default);
Assert(transport.Posts == 1, "following jobs do not retry failed service");
await Wait(() => service.Status.StartsWith("Connected"));
Assert(transport.Checks == 3, "independent scheduled health poll recovers");
await service.StopAsync(default);

sealed class FakeService : HttpMessageHandler {
    public volatile bool Healthy;
    public int Checks, Posts;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
        if (request.Method == HttpMethod.Get) { Interlocked.Increment(ref Checks); return Task.FromResult(new HttpResponseMessage(Healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"status\":\"" + (Healthy ? "ok" : "unavailable") + "\"}") }); }
        Interlocked.Increment(ref Posts);
        throw new HttpRequestException("Fixture connection failure");
    }
}
