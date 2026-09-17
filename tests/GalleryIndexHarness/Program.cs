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

if (args.Length == 2 && args[0] == "--inspect")
{
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = args[1], Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString());
    connection.Open();
    foreach (var table in new[] { "AspNetUsers", "UserRoots", "ShareLinks", "GalleryCollections", "GalleryIndexEntries", "GalleryIndexFolders" })
    {
        using var exists = connection.CreateCommand(); exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$table"; exists.Parameters.AddWithValue("$table", table);
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0) { Console.WriteLine($"{table}: absent"); continue; }
        using var command = connection.CreateCommand(); command.CommandText = $"SELECT count(*) FROM {table}";
        Console.WriteLine($"{table}: {command.ExecuteScalar()}");
    }
    using var setting = connection.CreateCommand(); setting.CommandText = "SELECT Value FROM AppSettings WHERE Key='BackgroundThumbnailWorkers'";
    Console.WriteLine($"Background workers setting: {setting.ExecuteScalar() ?? "0 (default)"}");
    using var errors = connection.CreateCommand(); errors.CommandText = "SELECT count(*) FROM GalleryIndexFolders WHERE Error <> ''";
    using var checkIndex = connection.CreateCommand(); checkIndex.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='GalleryIndexFolders'";
    if (Convert.ToInt32(checkIndex.ExecuteScalar()) > 0) Console.WriteLine($"Folders pending unavailable retry: {errors.ExecuteScalar()}");
    return;
}

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
static async Task Until(Func<bool> predicate) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); while (!predicate()) await Task.Delay(50, timeout.Token); }
var temp = Directory.CreateTempSubdirectory("gallery-index-test-").FullName;
try
{
    var root = Directory.CreateDirectory(Path.Combine(temp, "media")).FullName;
    var child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
    var cache = Path.Combine(temp, "cache");
    using (var image = new Image<Rgb24>(900, 600)) { image.SaveAsJpeg(Path.Combine(root, "one.jpg")); image.SaveAsJpeg(Path.Combine(child, "two.jpg")); }
    File.WriteAllText(Path.Combine(root, "Thumbs.db"), "ignored");
    var services = new ServiceCollection().AddLogging();
    services.AddDbContext<GalleryDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(temp, "test.db")}"));
    services.AddSingleton<IWebHostEnvironment>(new TestEnvironment(temp));
    services.AddSingleton<IOptions<GalleryOptions>>(Options.Create(new GalleryOptions { CachePath = cache }));
    var settings = new ThumbnailQueueSettings(2); settings.SetBackgroundWorkers(1);
    services.AddSingleton(settings);
    services.AddSingleton<GalleryIndexService>(); services.AddScoped<FileSystemService>();
    services.AddSingleton<ThumbnailWorkQueue>(); services.AddSingleton<ThumbnailService>(); services.AddSingleton<BackgroundThumbnailService>();
    await using var provider = services.BuildServiceProvider();
    var owner = new ApplicationUser { Id = "owner", UserName = "owner" };
    int rootId;
    using (var scope = provider.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries DROP COLUMN MediumThumbnailSignature");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries DROP COLUMN MediumRetryAfter");
        await GalleryIndexService.EnsureSchemaAsync(db); await GalleryIndexService.EnsureSchemaAsync(db);
        db.Users.Add(owner); var assignment = new UserRoot { OwnerUserId = owner.Id, PhysicalPath = root, Name = "Test" }; db.Add(assignment); await db.SaveChangesAsync(); rootId = assignment.Id;
    }
    var index = provider.GetRequiredService<GalleryIndexService>();
    var logical = FileSystemService.RootMarker(rootId);
    Check(index.Read(owner, logical)!.Count == 2, "Initial direct scan and metadata exclusion");
    index.Refresh(rootId, "child", true);
    Check(index.Covers(owner, logical + "/child").Count == 1, "Cover comes from indexed children");
    Check(index.Read(new ApplicationUser { Id = "stranger" }, logical) is null, "Owner boundary");
    File.WriteAllText(Path.Combine(root, "new.txt"), "new");
    Check(index.Read(owner, logical)!.Count == 2, "Cached read before asynchronous refresh");
    index.Refresh(rootId, "", true);
    Check(index.Read(owner, logical)!.Count == 3, "Refresh detects added file");
    var queue = provider.GetRequiredService<ThumbnailWorkQueue>();
    var worker = provider.GetRequiredService<BackgroundThumbnailService>();
    var thumbs = provider.GetRequiredService<ThumbnailService>();
    Check(index.Pending(thumbs.Signature, 20).Count == 2, "Only images pending");
    var vanished=Path.Combine(root,"vanished.jpg");File.Copy(Path.Combine(root,"one.jpg"),vanished);
    index.Refresh(rootId,"",true);
    var vanishedEntry=index.Pending(thumbs.Signature,20).Single(x=>x.Name=="vanished.jpg");
    File.Delete(vanished);index.RefreshAfterThumbnailFailure(vanishedEntry);
    Check(index.Pending(thumbs.Signature,20).Count==2,"Failed missing file reconciled immediately out of index");
    var changedEntry=index.Pending(thumbs.Signature,20).First(x=>x.Name=="one.jpg");
    File.SetLastWriteTimeUtc(Path.Combine(root,"one.jpg"),DateTime.UtcNow.AddSeconds(5));
    index.RefreshAfterThumbnailFailure(changedEntry);
    Check(index.Pending(thumbs.Signature,20).Any(x=>x.Name=="one.jpg"&&x.RetryAfter==0&&x.ModifiedTicks!=changedEntry.ModifiedTicks),"Changed failed file immediately eligible with new fingerprint");
    var retryEntry=index.Pending(thumbs.Signature,20).First();index.RefreshAfterThumbnailFailure(retryEntry,true);
    using(var scope=provider.CreateScope()) {
        var saved=scope.ServiceProvider.GetRequiredService<GalleryDbContext>().GalleryIndexEntries.Single(x=>x.RootId==retryEntry.RootId&&x.PathKey==retryEntry.PathKey);
        Check(saved.MediumRetryAfter>DateTimeOffset.UtcNow.ToUnixTimeSeconds() && saved.MediumRetryAfter<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+30,"Unchanged failure retries within30 seconds, not an hour");
        scope.ServiceProvider.GetRequiredService<GalleryDbContext>().GalleryIndexEntries.ExecuteUpdate(x=>x.SetProperty(e=>e.MediumRetryAfter,0L));
    }
    index.Defer(index.Pending(thumbs.Signature,20)[0]);
    Check(index.HasRunnableSmall(thumbs.Signature),"Eligible small still has priority");
    using(var scope=provider.CreateScope()) {
        var db=scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        db.GalleryIndexEntries.Where(x=>x.IsImage && x.RetryAfter==0).ExecuteUpdate(x=>x.SetProperty(e=>e.ThumbnailSignature,thumbs.Signature));
        await Until(()=>!index.HasRunnableSmall(thumbs.Signature)); // Direct test SQL bypasses normal cache invalidation.
        Check(index.HasPendingSmall(thumbs.Signature) && !index.HasRunnableSmall(thumbs.Signature),"Only deferred small remain");
        Check(index.PendingMedium(thumbs.Signature,thumbs.MediumSignature,20).Count==1,"Medium proceeds for ready-small image while another waits retry");
        db.GalleryIndexEntries.ExecuteUpdate(x=>x.SetProperty(e=>e.RetryAfter,0L));
        await Until(()=>index.HasRunnableSmall(thumbs.Signature));
        Check(index.HasRunnableSmall(thumbs.Signature) && index.PendingMedium(thumbs.Signature,thumbs.MediumSignature,20).Count==0,"Due small retry blocks the next medium feed");
        db.GalleryIndexEntries.ExecuteUpdate(x=>x.SetProperty(e=>e.ThumbnailSignature,""));
    }
    await queue.StartAsync(default); await worker.StartAsync(default);
    await Until(() => index.Pending(thumbs.Signature, 20).Count == 0);
    await Until(() => { using var scope=provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<GalleryDbContext>().GalleryIndexEntries.Count(x=>x.IsImage && x.MediumThumbnailSignature==thumbs.MediumSignature)==2; });
    Check(Directory.GetFiles(cache, "*.webp", SearchOption.AllDirectories).Length == 4, "Indexed worker generated small then medium for both images locally");
    var original = Path.Combine(root, "one.jpg");
    var old = new FileInfo(original);
    var oldSize = old.Length; var oldTicks = old.LastWriteTimeUtc.Ticks;
    File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddSeconds(10));
    index.Refresh(rootId, "", true);
    index.RecordThumbnail(owner.Id, original, oldSize, oldTicks, thumbs.Signature);
    index.RecordMediumThumbnail(owner.Id, original, oldSize, oldTicks, thumbs.MediumSignature);
    Check(index.PendingMedium(thumbs.Signature,thumbs.MediumSignature,20).Count==0,"New small fingerprint blocks medium feed again");
    Check(index.Pending(thumbs.Signature, 20).Count == 1, "Stale job cannot mark changed fingerprint ready");
    await Until(() => index.Pending(thumbs.Signature, 20).Count == 0);
    await index.StartAsync(default);
    await Until(() => index.Status.Contains("watchers"));
    File.WriteAllText(Path.Combine(root, "watched.txt"), "event");
    await Until(() => { using var scope = provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<GalleryDbContext>().GalleryIndexEntries.Any(x => x.Name == "watched.txt"); });
    await index.StopAsync(default);
    Directory.Move(child, Path.Combine(temp, "moved-child"));
    index.Refresh(rootId, "", true);
    using (var scope = provider.CreateScope())
        Check(!scope.ServiceProvider.GetRequiredService<GalleryDbContext>().GalleryIndexEntries.Any(x => x.Name == "two.jpg"), "Deleted directory removes indexed descendants");
    await worker.StopAsync(default);
    File.WriteAllText(Path.Combine(root,"clip.mp4"),"Index-only video fixture");
    index.Refresh(rootId,"",true);
    Check(index.Pending(thumbs.Signature,20).Any(x=>x.Name=="clip.mp4" && x.IsVideo),"Indexed videos enter the small background queue");
    Check(index.Covers(owner,logical).Any(x=>x.RelativePath.EndsWith("clip.mp4")),"Videos participate in folder covers");
    var videoInfo=new FileInfo(Path.Combine(root,"clip.mp4"));
    index.RecordThumbnail(owner.Id,videoInfo.FullName,videoInfo.Length,videoInfo.LastWriteTimeUtc.Ticks,thumbs.Signature);
    Check(index.PendingMedium(thumbs.Signature,thumbs.MediumSignature,20).Any(x=>x.Name=="clip.mp4"),"Small-ready videos enter medium queue");
    var progress=await index.ThumbnailProgressAsync(thumbs.Signature,thumbs.MediumSignature,default);
    Check(progress[owner.Id].Total==2,"Management totals include image and video");
    Directory.Move(root, Path.Combine(temp, "offline"));
    index.Refresh(rootId, "", true);
    Check(index.Read(owner, logical)!.Count >= 3, "Unavailable root must preserve prior index");
    await worker.StopAsync(default); await queue.StopAsync(default);
    Console.WriteLine("PASS: first scan, cached reads, refresh, watcher, authorization, pending-only worker, changed fingerprint race, subtree removal, unavailable root retention.");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(temp, true); }

sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Harness";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
