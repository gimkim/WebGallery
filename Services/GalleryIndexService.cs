using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.ViewModels;

namespace WebGallery.Services;

// Derived, rebuildable metadata only. Source access always resolves through FileSystemService.
public sealed class GalleryIndexService(IServiceScopeFactory scopes, IOptions<GalleryOptions> options,
    IWebHostEnvironment environment, ILogger<GalleryIndexService> logger, DateTakenIndexer? dateTakenIndexer = null) : BackgroundService
{
    private readonly object _write = new();
    private readonly object[] _folderLocks = Enumerable.Range(0, 256).Select(_ => new object()).ToArray();
    private readonly ConcurrentDictionary<(int Root, string Path), byte> _requested = new();
    private readonly Dictionary<int, (string Path, FileSystemWatcher Watcher)> _watchers = [];
    private readonly string _cache = Path.GetFullPath(options.Value.CachePath, environment.ContentRootPath);
    private readonly string _keys = Path.GetFullPath(options.Value.DataProtectionKeysPath, environment.ContentRootPath);
    private string? _database;
    private int _reconcileHours = 24;
    private int _overflow;
    private long _scanCount;
    private (string Signature, long Until, bool Runnable)? _smallProbe;
    private string _status = "Starting";
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private readonly SemaphoreSlim _indexProgressGate = new(1, 1);
    private (DateTimeOffset At, IndexProgress Value)? _indexProgress;
    public sealed record IndexProgress(long Total, long Completed, long WithDate, long WithoutDate, long Pending, long Retry,
        string FolderStatus, long FoldersChecked, DateTimeOffset UpdatedAt);
    public async Task<IndexProgress> IndexProgressAsync(CancellationToken token)
    {
        await _indexProgressGate.WaitAsync(token);
        try {
            if (_indexProgress is { } cached && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(10)) return cached.Value;
            using var scope = scopes.CreateScope(); var db = Db(scope);
            var counts = await (from entry in db.GalleryIndexEntries.AsNoTracking()
                join root in db.UserRoots on entry.RootId equals root.Id
                where entry.IsImage && !entry.IsDirectory && entry.OwnerId == root.OwnerUserId
                group entry by 1 into entries
                select new {
                    Total = entries.LongCount(),
                    Completed = entries.LongCount(x => x.DateTakenScanAfter == long.MaxValue),
                    WithDate = entries.LongCount(x => x.DateTakenScanAfter == long.MaxValue && x.DateTakenTicks != null),
                    WithoutDate = entries.LongCount(x => x.DateTakenScanAfter == long.MaxValue && x.DateTakenTicks == null),
                    Pending = entries.LongCount(x => x.DateTakenScanAfter == 0),
                    Retry = entries.LongCount(x => x.DateTakenScanAfter > 0 && x.DateTakenScanAfter < long.MaxValue)
                }).SingleOrDefaultAsync(token);
            var now = DateTimeOffset.UtcNow;
            var value = new IndexProgress(counts?.Total ?? 0, counts?.Completed ?? 0, counts?.WithDate ?? 0,
                counts?.WithoutDate ?? 0, counts?.Pending ?? 0, counts?.Retry ?? 0, Status, ScanCount, now);
            _indexProgress = (now, value);
            return value;
        } finally { _indexProgressGate.Release(); }
    }
    private (string Small, string Medium, DateTimeOffset At, Dictionary<string, ThumbnailProgress> Counts)? _progress;
    public sealed record ThumbnailProgress(string OwnerId, long Total, long SmallReady, long MediumReady);
    public async Task<Dictionary<string, ThumbnailProgress>> ThumbnailProgressAsync(string small, string medium, CancellationToken token) {
        await _progressGate.WaitAsync(token);
        try {
            if (_progress is { } cached && cached.Small == small && cached.Medium == medium && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(10)) return cached.Counts;
            using var scope = scopes.CreateScope(); var db = Db(scope);
            var counts = await (from entry in db.GalleryIndexEntries.AsNoTracking()
                join root in db.UserRoots on entry.RootId equals root.Id
                where (entry.IsImage || entry.IsVideo) && !entry.IsDirectory && entry.OwnerId == root.OwnerUserId
                group entry by entry.OwnerId into entries
                select new ThumbnailProgress(entries.Key, entries.LongCount(), entries.LongCount(e => e.ThumbnailSignature == small), entries.LongCount(e => e.MediumThumbnailSignature == medium)))
                .ToDictionaryAsync(x => x.OwnerId, token);
            _progress = (small, medium, DateTimeOffset.UtcNow, counts);
            return counts;
        } finally { _progressGate.Release(); }
    }
    public string Status => Volatile.Read(ref _status);
    public long ScanCount => Interlocked.Read(ref _scanCount);
    public int ReconcileHours => Volatile.Read(ref _reconcileHours);
    public void SetReconcileHours(int hours) => Volatile.Write(ref _reconcileHours, Math.Clamp(hours, 1, 168));
    public static string Key(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private GalleryDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
    private static string Logical(int root, string relative) => FileSystemService.RootMarker(root) + (relative.Length == 0 ? "" : "/" + relative);

    public static async Task EnsureSchemaAsync(GalleryDbContext db) {
        await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS GalleryIndexEntries (
            RootId INTEGER NOT NULL, PathKey TEXT NOT NULL, ParentKey TEXT NOT NULL,
            RelativePath TEXT NOT NULL, PhysicalKey TEXT NOT NULL, OwnerId TEXT NOT NULL,
            Name TEXT NOT NULL, IsDirectory INTEGER NOT NULL, IsImage INTEGER NOT NULL, IsVideo INTEGER NOT NULL,
            Extension TEXT NOT NULL, Size INTEGER NOT NULL, ModifiedTicks INTEGER NOT NULL,
            ThumbnailSignature TEXT NOT NULL, RetryAfter INTEGER NOT NULL,
            PRIMARY KEY (RootId, PathKey), FOREIGN KEY (RootId) REFERENCES UserRoots(Id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_GalleryIndexEntries_RootId_ParentKey ON GalleryIndexEntries(RootId,ParentKey);
        CREATE INDEX IF NOT EXISTS IX_GalleryIndexEntries_IsImage_RetryAfter_ThumbnailSignature ON GalleryIndexEntries(IsImage,RetryAfter,ThumbnailSignature);
        CREATE INDEX IF NOT EXISTS IX_GalleryIndexEntries_IsVideo_RetryAfter_ThumbnailSignature ON GalleryIndexEntries(IsVideo,RetryAfter,ThumbnailSignature);
        CREATE INDEX IF NOT EXISTS IX_GalleryIndexEntries_OwnerId_PhysicalKey ON GalleryIndexEntries(OwnerId,PhysicalKey);
        CREATE TABLE IF NOT EXISTS GalleryIndexFolders (
            RootId INTEGER NOT NULL, PathKey TEXT NOT NULL, RelativePath TEXT NOT NULL,
            PhysicalRoot TEXT NOT NULL, ScannedAt INTEGER NOT NULL, NextScan INTEGER NOT NULL,
            Revision TEXT NOT NULL, Error TEXT NOT NULL,
            PRIMARY KEY (RootId,PathKey), FOREIGN KEY (RootId) REFERENCES UserRoots(Id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_GalleryIndexFolders_NextScan ON GalleryIndexFolders(NextScan);
        """);
        await db.Database.OpenConnectionAsync();
        try {
            var columns = new HashSet<string>();
            using (var command = db.Database.GetDbConnection().CreateCommand()) {
                command.CommandText = "PRAGMA table_info(GalleryIndexEntries)";
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("MediumThumbnailSignature"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries ADD COLUMN MediumThumbnailSignature TEXT NOT NULL DEFAULT ''");
            if (!columns.Contains("MediumRetryAfter"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries ADD COLUMN MediumRetryAfter INTEGER NOT NULL DEFAULT 0");
            if (!columns.Contains("DateTakenTicks"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries ADD COLUMN DateTakenTicks INTEGER NULL");
            if (!columns.Contains("DateTakenScanAfter"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE GalleryIndexEntries ADD COLUMN DateTakenScanAfter INTEGER NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_GalleryIndexEntries_DateTakenScan ON GalleryIndexEntries(IsImage,DateTakenScanAfter)");
            // Shorten legacy one-hour deferrals on upgrade without changing ready fingerprints.
            foreach(var extension in RawPreview.Extensions) {
                var ext=extension[1..].ToUpperInvariant();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE GalleryIndexEntries SET IsImage=1 WHERE IsImage=0 AND IsDirectory=0 AND Extension={ext}");
            }
            await db.Database.ExecuteSqlRawAsync("UPDATE GalleryIndexEntries SET RetryAfter=unixepoch()+30 WHERE RetryAfter>unixepoch()+30");
            await db.Database.ExecuteSqlRawAsync("UPDATE GalleryIndexEntries SET MediumRetryAfter=unixepoch()+30 WHERE MediumRetryAfter>unixepoch()+30");
        } finally { await db.Database.CloseConnectionAsync(); }
    }

    public bool TryRoot(ApplicationUser owner, string logicalPath, out UserRoot root, out string relative)
    {
        var segments = FileSystemService.ToLogicalPath(logicalPath).Split('/', 2);
        relative = segments.Length > 1 ? segments[1] : "";
        root = null!;
        if (!segments[0].StartsWith("@root-") || !int.TryParse(segments[0][6..], out var id)) return false;
        using var scope = scopes.CreateScope();
        root = Db(scope).UserRoots.AsNoTracking().SingleOrDefault(x => x.Id == id && x.OwnerUserId == owner.Id)!;
        return root is not null;
    }

    public IReadOnlyList<GalleryItemViewModel>? Read(ApplicationUser owner, string logicalPath)
    {
        if (!TryRoot(owner, logicalPath, out var root, out var relative)) return null;
        using var scope = scopes.CreateScope();
        var db = Db(scope);
        var key = Key(relative);
        var folder = db.GalleryIndexFolders.AsNoTracking().SingleOrDefault(x => x.RootId == root.Id && x.PathKey == key);
        if (folder is null || folder.ScannedAt == 0 || !FileSystemService.PathsEqual(folder.PhysicalRoot, root.PhysicalPath))
            Refresh(root.Id, relative);
        Request(root.Id, relative);
        dateTakenIndexer?.RequestFolder(root.Id, key);
        var entries = db.GalleryIndexEntries.AsNoTracking().Where(x => x.RootId == root.Id && x.ParentKey == key).ToList();
        return entries.Select(entry => new GalleryItemViewModel(entry.Name, Logical(root.Id, entry.RelativePath),
            entry.IsDirectory, entry.IsImage, entry.IsVideo, entry.Size, new DateTimeOffset(entry.ModifiedTicks, TimeSpan.Zero), entry.Extension,
            entry.IsDirectory ? Covers(owner, Logical(root.Id, entry.RelativePath)) : [],
            entry.DateTakenTicks is long ticks ? new DateTime(ticks) : null)).ToList();
    }

    public IReadOnlyList<ThumbnailSourceViewModel> Covers(ApplicationUser owner, string logicalPath)
    {
        if (!TryRoot(owner, logicalPath, out var root, out var relative)) return [];
        using var scope = scopes.CreateScope();
        var db = Db(scope); var key = Key(relative);
        if (!db.GalleryIndexFolders.Any(x => x.RootId == root.Id && x.PathKey == key && x.PhysicalRoot == root.PhysicalPath && x.ScannedAt > 0))
        { Request(root.Id, relative); return []; }
        return db.GalleryIndexEntries.AsNoTracking().Where(x => x.RootId == root.Id && x.ParentKey == key && (x.IsImage || x.IsVideo))
            .OrderBy(x => x.Name).Take(4).ToList()
            .Select(x => new ThumbnailSourceViewModel(Logical(root.Id, x.RelativePath), FileSystemService.CreateThumbnailCacheStamp(x.Size, x.ModifiedTicks))).ToList();
    }

    public void Request(int rootId, string relative)
    {
        if (_requested.Count < 4096) _requested.TryAdd((rootId, relative), 0);
        else Interlocked.Exchange(ref _overflow, 1);
    }

    public void Rebuild()
    {
        lock (_write)
        {
            using var scope = scopes.CreateScope();
            var db = Db(scope);
            using var transaction = db.Database.BeginTransaction();
            db.GalleryIndexFolders.ExecuteUpdate(x => x.SetProperty(f => f.NextScan, 0L));
            // Re-read even unchanged files and previous no-EXIF/error results. Keep known
            // dates and thumbnail readiness available until fresh metadata replaces them.
            db.GalleryIndexEntries.Where(x => x.IsImage && !x.IsDirectory)
                .ExecuteUpdate(x => x.SetProperty(e => e.DateTakenScanAfter, 0L));
            transaction.Commit();
        }
        Interlocked.Exchange(ref _overflow, 1);
    }

    public void Refresh(int rootId, string relative, bool force = false, CancellationToken cancellation = default)
    {
        var key = Key(relative);
        lock (_folderLocks[(HashCode.Combine(rootId, key) & int.MaxValue) % _folderLocks.Length])
        {
            using var scope = scopes.CreateScope();
            var db = Db(scope);
            var root = db.UserRoots.AsNoTracking().SingleOrDefault(x => x.Id == rootId);
            if (root is null) return;
            var previous = db.GalleryIndexFolders.AsNoTracking().SingleOrDefault(x => x.RootId == rootId && x.PathKey == key);
            // Coalesce concurrent opens and watcher bursts. Force is used for due/rebuild scans.
            if (!force && previous is { ScannedAt: > 0 } && previous.ScannedAt > Now - 3 && FileSystemService.PathsEqual(previous.PhysicalRoot, root.PhysicalPath)) return;
            List<GalleryIndexEntry> found = [];
            try
            {
                var files = new FileSystemService(db);
                var path = files.ResolvePath(new ApplicationUser { Id = root.OwnerUserId }, Logical(rootId, relative));
                if (Excluded(path) || FileSystemService.IsIgnoredFileSystemEntry(path) || FileSystemService.IsReparsePoint(path))
                    throw new DirectoryNotFoundException();
                foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Excluded(item.FullName) || FileSystemService.IsIgnoredFileSystemEntry(item.FullName) || FileSystemService.IsReparsePoint(item.FullName)) continue;
                    try
                    {
                        var directory = (item.Attributes & FileAttributes.Directory) != 0;
                        var child = relative.Length == 0 ? item.Name : relative + "/" + item.Name;
                        var extension = directory ? "" : Path.GetExtension(item.Name);
                        found.Add(new GalleryIndexEntry { RootId = rootId, PathKey = Key(child), ParentKey = key, RelativePath = child,
                            PhysicalKey = Key(item.FullName), OwnerId = root.OwnerUserId, Name = item.Name, IsDirectory = directory,
                            IsImage = !directory && FileSystemService.IsImage(extension), IsVideo = !directory && FileSystemService.IsVideo(extension),
                            Extension = extension.TrimStart('.').ToUpperInvariant(), Size = directory ? 0 : ((FileInfo)item).Length, ModifiedTicks = item.LastWriteTimeUtc.Ticks });
                    }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                // Never interpret an unavailable mount or an incomplete enumeration as an empty folder.
                lock (_write)
                {
                    db.GalleryIndexFolders.Where(x => x.RootId == rootId && x.PathKey == key)
                        .ExecuteUpdate(x => x.SetProperty(f => f.NextScan, Now + 300).SetProperty(f => f.Error, "Folder unavailable; retrying"));
                }
                return;
            }
            lock (_write)
            {
                using var transaction = db.Database.BeginTransaction();
                var current = db.UserRoots.AsNoTracking().SingleOrDefault(x => x.Id == rootId);
                if (current is null || !FileSystemService.PathsEqual(current.PhysicalPath, root.PhysicalPath) || current.OwnerUserId != root.OwnerUserId) return;
                if (previous is not null && !FileSystemService.PathsEqual(previous.PhysicalRoot, root.PhysicalPath))
                {
                    db.GalleryIndexEntries.Where(x => x.RootId == rootId).ExecuteDelete();
                    db.GalleryIndexFolders.Where(x => x.RootId == rootId).ExecuteDelete();
                }
                var folder = db.GalleryIndexFolders.SingleOrDefault(x => x.RootId == rootId && x.PathKey == key)
                    ?? new GalleryIndexFolder { RootId = rootId, PathKey = key, RelativePath = relative, PhysicalRoot = root.PhysicalPath };
                if (db.Entry(folder).State == EntityState.Detached) db.Add(folder);
                var old = db.GalleryIndexEntries.Where(x => x.RootId == rootId && x.ParentKey == key).ToDictionary(x => x.PathKey);
                var changed = folder.ScannedAt == 0;
                foreach (var item in found)
                {
                    if (old.Remove(item.PathKey, out var existing))
                    {
                        if (existing.ModifiedTicks != item.ModifiedTicks || existing.Size != item.Size || existing.IsDirectory != item.IsDirectory || existing.Name != item.Name || existing.IsImage != item.IsImage || existing.IsVideo != item.IsVideo)
                        {
                            if (existing.IsDirectory && !item.IsDirectory) DeleteSubtree(db, rootId, existing.PathKey);
                            db.Entry(existing).CurrentValues.SetValues(item);
                            changed = true;
                        }
                    }
                    else { db.Add(item); changed = true; }
                    if (item.IsDirectory && !db.GalleryIndexFolders.Any(x => x.RootId == rootId && x.PathKey == item.PathKey))
                        db.Add(new GalleryIndexFolder { RootId = rootId, PathKey = item.PathKey, RelativePath = item.RelativePath, PhysicalRoot = root.PhysicalPath });
                }
                foreach (var removed in old.Values)
                {
                    if (removed.IsDirectory) DeleteSubtree(db, rootId, removed.PathKey);
                    db.Remove(removed); changed = true;
                }
                folder.ScannedAt = Now; folder.NextScan = Now + ReconcileHours * 3600L; folder.Error = "";
                if (changed) folder.Revision = Guid.NewGuid().ToString("N");
                db.SaveChanges(); transaction.Commit(); _smallProbe = null;
            }
            Interlocked.Increment(ref _scanCount);
        }
    }

    private static void DeleteSubtree(GalleryDbContext db, int rootId, string key)
    {
        var prefix = key + "/";
        // SQLite LIKE/StartsWith is case-insensitive even on Linux; substr equality is ordinal.
        db.GalleryIndexEntries.Where(x => x.RootId == rootId && x.PathKey.Substring(0, prefix.Length) == prefix).ExecuteDelete();
        db.GalleryIndexFolders.Where(x => x.RootId == rootId && (x.PathKey == key || x.PathKey.Substring(0, prefix.Length) == prefix)).ExecuteDelete();
    }

    private bool Excluded(string path) => FileSystemService.PathsEqual(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(_cache))
        || path.StartsWith(Path.TrimEndingDirectorySeparator(_cache) + Path.DirectorySeparatorChar, FileSystemService.PathComparison)
        || FileSystemService.PathsEqual(path, _keys)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(_keys) + Path.DirectorySeparatorChar, FileSystemService.PathComparison)
        || (_database is not null && (FileSystemService.PathsEqual(path, _database) || FileSystemService.PathsEqual(path, _database + "-wal") || FileSystemService.PathsEqual(path, _database + "-shm")));

    public List<GalleryIndexEntry> Pending(string signature, int count)
    {
        using var scope = scopes.CreateScope();
        return Db(scope).GalleryIndexEntries.AsNoTracking().Where(x => (x.IsImage || x.IsVideo) && x.ThumbnailSignature != signature && x.RetryAfter <= Now)
            .OrderBy(x => x.RetryAfter).ThenBy(x => x.RootId).ThenBy(x => x.PathKey).Take(count).ToList();
    }

    // Total unfinished small images, distinct from runnable work during retry backoff.
    public bool HasPendingSmall(string signature) {
        using var scope = scopes.CreateScope();
        return Db(scope).GalleryIndexEntries.Any(x => (x.IsImage || x.IsVideo) && x.ThumbnailSignature != signature);
    }
    public bool HasRunnableSmall(string signature) {
        lock (_write) {
            var now = Environment.TickCount64;
            if (_smallProbe is { } cached && cached.Signature == signature && now < cached.Until) return cached.Runnable;
            using var scope = scopes.CreateScope();
            var runnable = Db(scope).GalleryIndexEntries.Any(x => (x.IsImage || x.IsVideo) && x.ThumbnailSignature != signature && x.RetryAfter <= Now);
            _smallProbe = (signature, now + 250, runnable);
            return runnable;
        }
    }

    public List<GalleryIndexEntry> PendingMedium(string smallSignature, string signature, int count) {
        using var scope = scopes.CreateScope();
        var entries = Db(scope).GalleryIndexEntries.AsNoTracking();
        return entries.Where(x => (x.IsImage || x.IsVideo) && x.ThumbnailSignature == smallSignature && x.MediumThumbnailSignature != signature && x.MediumRetryAfter <= Now
                && !entries.Any(s => (s.IsImage || s.IsVideo) && s.ThumbnailSignature != smallSignature && s.RetryAfter <= Now))
            .OrderBy(x => x.MediumRetryAfter).ThenBy(x => x.RootId).ThenBy(x => x.PathKey).Take(count).ToList();
    }

    public void RecordMediumThumbnail(string ownerId, string path, long size, long ticks, string signature) {
        var physical = Key(Path.GetFullPath(path));
        lock (_write) {
            using var scope = scopes.CreateScope();
            Db(scope).GalleryIndexEntries.Where(x => x.PhysicalKey == physical && x.Size == size && x.ModifiedTicks == ticks && x.MediumThumbnailSignature != signature)
                .ExecuteUpdate(x => x.SetProperty(e => e.MediumThumbnailSignature, signature).SetProperty(e => e.MediumRetryAfter, 0L));
        }
    }
    public void TryRecordMediumThumbnail(string ownerId, string path, long size, long ticks, string signature) {
        try { RecordMediumThumbnail(ownerId, path, size, ticks, signature); }
        catch (Exception ex) { logger.LogDebug(ex, "Medium thumbnail delivered; index readiness will retry in background"); }
    }

    public void RecordThumbnail(string ownerId, string path, long size, long ticks, string signature)
    {
        var physical = Key(Path.GetFullPath(path));
        lock (_write)
        {
            using var scope = scopes.CreateScope();
            Db(scope).GalleryIndexEntries.Where(x => x.PhysicalKey == physical && x.Size == size && x.ModifiedTicks == ticks && x.ThumbnailSignature != signature)
                .ExecuteUpdate(x => x.SetProperty(e => e.ThumbnailSignature, signature).SetProperty(e => e.RetryAfter, 0L));
            _smallProbe = null;
        }
    }

    public void TryRecordThumbnail(string ownerId, string path, long size, long ticks, string signature)
    {
        try { RecordThumbnail(ownerId, path, size, ticks, signature); }
        catch (Exception ex) { logger.LogDebug(ex, "Thumbnail delivered; derived index readiness update will retry in background"); }
    }

    public void Defer(GalleryIndexEntry entry, bool medium = false)
    {
        lock (_write)
        {
            using var scope = scopes.CreateScope();
            var query = Db(scope).GalleryIndexEntries.Where(x => x.RootId == entry.RootId && x.PathKey == entry.PathKey && x.Size == entry.Size && x.ModifiedTicks == entry.ModifiedTicks);
            if (medium) query.ExecuteUpdate(x => x.SetProperty(e => e.MediumRetryAfter, Now + 30));
            else query.ExecuteUpdate(x => x.SetProperty(e => e.RetryAfter, Now + 30));
            if (!medium) _smallProbe = null;
        }
    }

    public void RefreshAfterThumbnailFailure(GalleryIndexEntry entry, bool medium = false) {
        // Defer the old fingerprint first. A changed file discovered by Refresh gets fresh
        // signatures and retry=0; a confirmed missing child is removed by reconciliation.
        Defer(entry, medium);
        var parent = FileSystemService.GetParent(entry.RelativePath) ?? "";
        Refresh(entry.RootId, parent, true);
        if (parent.Length > 0 && !Directory.Exists(Path.GetDirectoryName(entry.PhysicalKey)))
            Refresh(entry.RootId, FileSystemService.GetParent(parent) ?? "", true);
        // Refresh deliberately retains the last snapshot on unavailable mounts/access failures.
    }

    private void SynchronizeRoots()
    {
        using var scope = scopes.CreateScope();
        var db = Db(scope); var roots = db.UserRoots.AsNoTracking().ToList();
        _database = Path.GetFullPath(db.Database.GetDbConnection().DataSource, environment.ContentRootPath);
        foreach (var id in _watchers.Keys.ToList())
            if (!roots.Any(x => x.Id == id && FileSystemService.PathsEqual(x.PhysicalPath, _watchers[id].Path)))
            { _watchers[id].Watcher.Dispose(); _watchers.Remove(id); }
        foreach (var root in roots)
        {
            lock (_write)
            {
                var folder = db.GalleryIndexFolders.SingleOrDefault(x => x.RootId == root.Id && x.PathKey == "");
                if (folder is not null && !FileSystemService.PathsEqual(folder.PhysicalRoot, root.PhysicalPath))
                {
                    db.GalleryIndexEntries.Where(x => x.RootId == root.Id).ExecuteDelete();
                    db.GalleryIndexFolders.Where(x => x.RootId == root.Id).ExecuteDelete();
                    db.ChangeTracker.Clear(); folder = null;
                }
                if (folder is null)
                { db.Add(new GalleryIndexFolder { RootId = root.Id, PhysicalRoot = root.PhysicalPath }); db.SaveChanges(); }
            }
            if (_watchers.ContainsKey(root.Id) || _watchers.Count >= 128 || !Directory.Exists(root.PhysicalPath)) continue;
            try
            {
                var watcher = new FileSystemWatcher(root.PhysicalPath) { IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes };
                void Change(string path)
                {
                    if (Excluded(path)) return;
                    var parent = Path.GetDirectoryName(path);
                    if (parent is null) return;
                    var relative = Path.GetRelativePath(root.PhysicalPath, parent).Replace(Path.DirectorySeparatorChar, '/');
                    if (relative == ".") relative = "";
                    if (relative == ".." || relative.StartsWith("../")) return;
                    Request(root.Id, relative);
                    if (Directory.Exists(path)) Request(root.Id, Path.GetRelativePath(root.PhysicalPath, path).Replace(Path.DirectorySeparatorChar, '/'));
                }
                watcher.Changed += (_, e) => Change(e.FullPath); watcher.Created += (_, e) => Change(e.FullPath);
                watcher.Deleted += (_, e) => Change(e.FullPath);
                watcher.Renamed += (_, e) => { Change(e.OldFullPath); Change(e.FullPath); };
                watcher.Error += (_, _) => Interlocked.Exchange(ref _overflow, 1);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(root.Id, (root.PhysicalPath, watcher));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { logger.LogWarning(ex, "Index watcher unavailable for root {RootId}; periodic scans remain active", root.Id); }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long rootsChecked = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (Now - rootsChecked >= 30) { SynchronizeRoots(); rootsChecked = Now; }
                    if (Interlocked.Exchange(ref _overflow, 0) != 0)
                    { lock (_write) { using var scope = scopes.CreateScope(); Db(scope).GalleryIndexFolders.ExecuteUpdate(x => x.SetProperty(f => f.NextScan, 0L)); } }
                    var requests = _requested.Keys.Take(16).ToList();
                    foreach (var request in requests)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        _requested.TryRemove(request, out _);
                        Refresh(request.Root, request.Path, force: true, stoppingToken);
                    }
                    using var queryScope = scopes.CreateScope();
                    var due = Db(queryScope).GalleryIndexFolders.AsNoTracking().Where(x => x.NextScan <= Now).OrderBy(x => x.NextScan).Take(8).ToList();
                    foreach (var folder in due) { stoppingToken.ThrowIfCancellationRequested(); Refresh(folder.RootId, folder.RelativePath, force: true, stoppingToken); }
                    Volatile.Write(ref _status, $"Index active · {ScanCount} folders checked this session · {_watchers.Count} watchers");
                    await Task.Delay(due.Count > 0 ? 100 : 1000, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogWarning(ex, "Index update failed; retrying"); Volatile.Write(ref _status, "Index update failed; retrying"); await Task.Delay(5000, stoppingToken); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { foreach (var watcher in _watchers.Values) watcher.Watcher.Dispose(); _watchers.Clear(); }
    }
}
