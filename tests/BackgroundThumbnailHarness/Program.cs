using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
var settings = new ThumbnailQueueSettings(1);
settings.SetBackgroundWorkers(1);
using (var queue = new ThumbnailWorkQueue(settings))
{
    await queue.StartAsync(default);
    var started = Gate(); var release = Gate(); var order = new List<string>();
    var blocker = queue.EnqueueAsync(async ct => { started.SetResult(); await release.Task.WaitAsync(ct); return 0; }, ThumbnailPriority.Visible, default);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var background = queue.EnqueueAsync(ct => { order.Add("background"); return Task.FromResult(0); }, ThumbnailPriority.Background, default);
    var normal = queue.EnqueueAsync(ct => { order.Add("normal"); return Task.FromResult(0); }, ThumbnailPriority.Normal, default);
    var visible = queue.EnqueueAsync(ct => { order.Add("visible"); return Task.FromResult(0); }, ThumbnailPriority.Visible, default);
    release.SetResult();
    await Task.WhenAll(blocker, background, normal, visible).WaitAsync(TimeSpan.FromSeconds(10));
    Check(order.SequenceEqual(new[] { "visible", "normal", "background" }), "Foreground must dispatch first.");
    settings.SetBackgroundWorkers(0);
    using var canceled = new CancellationTokenSource();
    var pending = queue.EnqueueAsync(ct => Task.FromResult(0), ThumbnailPriority.Background, canceled.Token);
    canceled.Cancel();
    try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Expected canceled background job"); }
    catch (OperationCanceledException) { }
    await queue.StopAsync(default);
}

var directory = Directory.CreateTempSubdirectory("webgallery-background-test-").FullName;
var limits = new ThumbnailQueueSettings(3);
limits.SetBackgroundWorkers(2);
using (var queue = new ThumbnailWorkQueue(limits))
{
    await queue.StartAsync(default);
    var twoStarted = Gate(); var visibleStarted = Gate(); var finish = Gate();
    int active = 0, backgroundActive = 0, peak = 0, backgroundPeak = 0;
    async Task<int> Job(bool background, CancellationToken ct)
    {
        var count = Interlocked.Increment(ref active);
        InterlockedExtensionsMax(ref peak, count);
        if (background)
        {
            count = Interlocked.Increment(ref backgroundActive);
            InterlockedExtensionsMax(ref backgroundPeak, count);
            if (count == 2) twoStarted.TrySetResult();
        }
        else visibleStarted.TrySetResult();
        await finish.Task.WaitAsync(ct);
        if (background) Interlocked.Decrement(ref backgroundActive);
        Interlocked.Decrement(ref active);
        return 0;
    }
    var jobs = Enumerable.Range(0, 8).Select(_ => queue.EnqueueAsync(ct => Job(true, ct), ThumbnailPriority.Background, default)).ToList();
    await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    jobs.Add(queue.EnqueueAsync(ct => Job(false, ct), ThumbnailPriority.Visible, default));
    await visibleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    finish.SetResult();
    await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(10));
    Check(peak <= 3 && backgroundPeak <= 2, "Background and total worker limits must both hold.");
    await queue.StopAsync(default);
}
try
{
    var root = Directory.CreateDirectory(Path.Combine(directory, "root")).FullName;
    var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
    var cache = Path.Combine(root, "cache");
    using (var image = new Image<Rgba32>(1200, 800))
    {
        await image.SaveAsJpegAsync(Path.Combine(root, "one.jpg"));
        await image.SaveAsJpegAsync(Path.Combine(child, "two.jpg"));
    }
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<GalleryDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(directory, "test.db")}"));
    services.AddScoped<FileSystemService>();
    services.AddSingleton<IWebHostEnvironment>(new TestEnvironment(directory));
    services.AddSingleton<IOptions<GalleryOptions>>(Options.Create(new GalleryOptions { CachePath = cache }));
    var runtime = new ThumbnailQueueSettings(4);
    services.AddSingleton(runtime);
    services.AddSingleton<ThumbnailWorkQueue>();
    services.AddSingleton<ThumbnailService>();
    services.AddSingleton<BackgroundThumbnailService>();
    await using var provider = services.BuildServiceProvider();
    using (var scope = provider.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner" });
        db.UserRoots.AddRange(new UserRoot { OwnerUserId = "owner", Name = "Root", PhysicalPath = root },
            new UserRoot { OwnerUserId = "owner", Name = "Overlap", PhysicalPath = child },
            new UserRoot { OwnerUserId = "owner", Name = "Missing", PhysicalPath = Path.Combine(directory, "missing") });
        await db.SaveChangesAsync();
    }
    var workQueue = provider.GetRequiredService<ThumbnailWorkQueue>();
    var scanner = provider.GetRequiredService<BackgroundThumbnailService>();
    var thumbnails = provider.GetRequiredService<ThumbnailService>();
    await workQueue.StartAsync(default);
    await scanner.StartAsync(default);
    runtime.SetBackgroundWorkers(3);
    var requests = Enumerable.Range(0, 60).Select(async i =>
    {
        using var ct = new CancellationTokenSource();
        if (i % 3 == 0) ct.Cancel();
        try { return await thumbnails.GetOrCreateAsync("owner", Path.Combine(root, "one.jpg"), ThumbnailPriority.Visible, ct.Token); }
        catch (OperationCanceledException) { return null; }
    }).ToArray();
    await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(20));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while (!scanner.Status.StartsWith("Pass complete")) await Task.Delay(30, timeout.Token);
    var files = Directory.GetFiles(cache, "*.webp", SearchOption.AllDirectories);
    Check(files.Length == 2, "Overlapping roots and concurrent requests must reuse exactly two cache files.");
    Check(!Directory.EnumerateFiles(cache, "*.new", SearchOption.AllDirectories).Any(), "No partial cache files.");
    foreach (var file in files) using (var image = await Image.LoadAsync(file)) Check(image.Width == 480 && image.Height == 320, "Valid complete WebP.");
    Check(BackgroundThumbnailService.EnumerateImages(root, default, cache).Count() == 2, "Scanner must exclude its own cache tree.");
    var cached = await thumbnails.GetOrCreateAsync("owner", Path.Combine(root, "one.jpg"), ThumbnailPriority.Visible, default);
    var written = File.GetLastWriteTimeUtc(cached);
    await thumbnails.GetOrCreateAsync("owner", Path.Combine(root, "one.jpg"), ThumbnailPriority.Background, default);
    Check(File.GetLastWriteTimeUtc(cached) == written, "Cache hit must not rewrite image.");
    runtime.SetBackgroundWorkers(0);
    await scanner.StopAsync(default);
    var remoteOptions = Options.Create(new GalleryOptions { CachePath = cache, ResizerServiceUrl = "https://fixture-one/ThumbService" });
    using var remote = new RemoteResizer(remoteOptions);
    var shared = new ThumbnailService(remoteOptions,new TestEnvironment(directory),workQueue,runtime,remote:remote);
    var changes = 0; runtime.Changed += () => changes++;
    remote.UpdateDecodeSettings("full",8,3);
    Check(remote.Workers == 8 && remote.BackgroundWorkers == 3, "Remote limits are independent");
    var sharedFull = await shared.GetOrCreateAsync("owner",Path.Combine(root,"one.jpg"),ThumbnailPriority.Visible,default);
    Check(sharedFull == cached && File.GetLastWriteTimeUtc(cached) == written,"Remote configuration must reuse original local Full cache without rewriting");
    runtime.SetReducedJpeg(true);
    Check(await shared.GetOrCreateAsync("owner",Path.Combine(root,"one.jpg"),ThumbnailPriority.Visible,default) == cached,"Unused local mode must not alter remote Full cache identity");
    remote.UpdateDecodeSettings("jpeg-idct",8,3);
    var sharedFast = await shared.GetOrCreateAsync("owner",Path.Combine(root,"one.jpg"),ThumbnailPriority.Visible,default);
    Check(sharedFast != cached,"Full and Fast must remain separate");
    var localOnly = new ThumbnailService(Options.Create(new GalleryOptions { CachePath = cache }),new TestEnvironment(directory),workQueue,new ThumbnailQueueSettings(),remote:null);
    Check(await localOnly.GetOrCreateAsync("owner",Path.Combine(root,"one.jpg"),ThumbnailPriority.Visible,default,"contain-idct-v1") == sharedFast,"Offline remote fallback and local Fast must share exactly the same file");
    remote.UpdateDecodeSettings("full",4,1);
    Check(await shared.GetOrCreateAsync("owner",Path.Combine(root,"one.jpg"),ThumbnailPriority.Visible,default) == cached,"Switching back must reuse original Full file");
    Check(changes >= 3,"Remote limits/modes and local mode changes must wake background scanner");
    var legacySource = Path.Combine(root,"legacy.jpg"); File.Copy(Path.Combine(root,"one.jpg"),legacySource);
    var legacyInfo = new FileInfo(legacySource);
    var legacyIdentity = $"contain-v1{remote.GetCacheSuffix(78,false)}|owner|{legacyInfo.FullName}|{legacyInfo.Length}|{legacyInfo.LastWriteTimeUtc.Ticks}|480x360|78";
    var legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyIdentity))).ToLowerInvariant();
    var legacyPath = Path.Combine(cache,legacyHash[..2],legacyHash + ".webp"); Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!); File.WriteAllBytes(legacyPath, new byte[] { 1, 2, 3 });
    var promoted = await shared.GetOrCreateAsync("owner",legacySource,ThumbnailPriority.Visible,default);
    Check(promoted != legacyPath && File.ReadAllBytes(legacyPath).SequenceEqual(new byte[] { 1, 2, 3 }) && (await Image.IdentifyAsync(promoted)).Width > 0,"Legacy cache is ignored and untouched; missing shared cache generates valid WebP");
    Console.WriteLine("PASS canonical remote/local cache reuse, Full/Fast separation, original file preservation, fallback quality and remote background/concurrent separation");
    await workQueue.StopAsync(default);
    Console.WriteLine("PASS: priority, disabled/canceled background queue, multi-root scan, concurrent on-demand/cancellation, overlapping-root cache reuse, WebP integrity, cache exclusion and graceful shutdown.");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }

static void InterlockedExtensionsMax(ref int target, int value)
{
    int old;
    do { old = Volatile.Read(ref target); if (old >= value) return; }
    while (Interlocked.CompareExchange(ref target, value, old) != old);
}

sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Harness";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
