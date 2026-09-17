using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using WebGallery.Models;
using WebGallery.Data;
using System.Text.Json;

namespace WebGallery.Services;

public sealed class UploadSessions(IOptions<GalleryOptions> options, IWebHostEnvironment environment, IServiceScopeFactory scopes) : BackgroundService
{
    public record PublishJournal(string Id, string Owner, int RootId, string RootPath, string Parent);
    public static string PublishName(string id) => ".webgallery-upload-"+id+".pending";
    public static string JournalPath(Session session) => session.Temp+".wg-publish.json";
    public const int ChunkSize = 4 * 1024 * 1024;
    public readonly SemaphoreSlim Mutations = new(1, 1);
    public sealed class Session
    {
        public required string Id, Owner, Destination, Relative, RootPath, Temp;
        public required int RootId;
        public required long Size;
        public long Offset;
        public bool Complete, Cancelled;
        public DateTime Updated = DateTime.UtcNow;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    public Session Start(string owner, UserRoot root, string destination, string relative, long size)
    {
        lock (sessions) {
            if (sessions.Values.Count(s => s.Owner == owner && !s.Complete && !s.Cancelled) >= 20
                || sessions.Values.Count(s => !s.Complete && !s.Cancelled) >= 200 || sessions.Count >= 20000)
                throw new InvalidOperationException("Too many upload sessions. Finish or cancel existing uploads first.");
            if (size < 0 || size > 1024L * 1024 * 1024 * 1024) throw new InvalidOperationException("File size must not exceed 1 TiB.");
            var id = Guid.NewGuid().ToString("N");
            var folder = Path.Combine(Path.GetFullPath(options.Value.CachePath, environment.ContentRootPath), "upload-staging");
            Directory.CreateDirectory(folder);
            var session = new Session { Id=id, Owner=owner, RootId=root.Id, RootPath=root.PhysicalPath, Destination=destination,
                Relative=WritePaths.Relative(relative), Size=size, Temp=Path.Combine(folder,"webgallery-upload-"+id + ".wg-upload-segment") };
            using (new FileStream(session.Temp, FileMode.CreateNew)) { }
            sessions[id] = session; return session;
        }
    }
    public Session Get(string owner, string id) => sessions.TryGetValue(id, out var session) && session.Owner == owner
        ? session : throw new KeyNotFoundException("Upload session expired. Start the upload again.");
    public bool HasActiveUploadInside(int rootId, string path) => sessions.Values.Any(s => s.RootId == rootId && !s.Complete && !s.Cancelled
        && FileSystemService.IsWithinShareScope(path,s.Destination+"/"+s.Relative));
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            try { await CleanupAsync(stoppingToken); }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
        }
    }
    public async Task CleanupAsync(CancellationToken stoppingToken = default)
    {
            foreach (var pair in sessions) {
                var s = pair.Value;
                if (DateTime.UtcNow - s.Updated < TimeSpan.FromHours(24) || !await s.Gate.WaitAsync(0, stoppingToken)) continue;
                try { if (File.Exists(s.Temp)) File.Delete(s.Temp); sessions.TryRemove(pair.Key, out _); }
                catch (Exception e) when(e is IOException or UnauthorizedAccessException) { } finally { s.Gate.Release(); }
            }
            // Staging files abandoned by an application restart have no live session.
            var folder = Path.Combine(Path.GetFullPath(options.Value.CachePath, environment.ContentRootPath), "upload-staging");
            if (Directory.Exists(folder)) foreach (var file in Directory.EnumerateFiles(folder, "webgallery-upload-*.wg-upload-segment")) {
                var name=Path.GetFileNameWithoutExtension(file);
                if (name.Length!=50 || !Guid.TryParseExact(name[18..],"N",out _)) continue;
                if (!sessions.ContainsKey(name[18..]) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                    try { File.Delete(file); } catch (IOException) { }
            }
            if (Directory.Exists(folder)) foreach (var file in Directory.EnumerateFiles(folder, "webgallery-upload-*.wg-upload-segment.wg-publish.json")) {
                if (File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddDays(-1)) continue;
                try {
                    var journal=JsonSerializer.Deserialize<PublishJournal>(await File.ReadAllTextAsync(file,stoppingToken));
                    if(journal is null || !Guid.TryParseExact(journal.Id,"N",out _) || sessions.ContainsKey(journal.Id))continue;
                    using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
                    var root=db.UserRoots.SingleOrDefault(x=>x.Id==journal.RootId && x.OwnerUserId==journal.Owner);
                    if(root is null || !FileSystemService.PathsEqual(root.PhysicalPath,journal.RootPath))continue;
                    var parent=WritePaths.Resolve(root,journal.Parent);
                    var staging=Path.Combine(parent,PublishName(journal.Id));
                    if(File.Exists(staging) && !FileSystemService.IsReparsePoint(staging))File.Delete(staging);
                    File.Delete(file);
                } catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { }
            }
    }
}
