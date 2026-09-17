using Microsoft.EntityFrameworkCore;
using WebGallery.Data;
using WebGallery.Models;

namespace WebGallery.Services;

public sealed class BackgroundThumbnailService(
    IServiceScopeFactory scopes, ThumbnailService thumbnails, ThumbnailQueueSettings settings,
    ILogger<BackgroundThumbnailService> logger, GalleryIndexService? index = null) : BackgroundService
{
    private readonly SemaphoreSlim _changed = new(0, 1);
    private string _status = "Disabled";
    public string Status => Volatile.Read(ref _status);
    private void Wake() { try { _changed.Release(); } catch (SemaphoreFullException) { } }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        settings.Changed += Wake;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (settings.EffectiveBackgroundWorkers == 0)
                {
                    Volatile.Write(ref _status, "Disabled");
                    await _changed.WaitAsync(stoppingToken);
                    continue;
                }
                using var passCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var changeCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var pass = RunPassAsync(passCancellation.Token);
                var changed = _changed.WaitAsync(changeCancellation.Token);
                if (await Task.WhenAny(pass, changed) == changed) passCancellation.Cancel();
                else changeCancellation.Cancel();
                try { await pass; }
                catch (OperationCanceledException) when (passCancellation.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Background thumbnail scan failed; will retry.");
                    Volatile.Write(ref _status, "Background work failed; retrying");
                }
                try { await changed; } catch (OperationCanceledException) when (changeCancellation.IsCancellationRequested) { }
                if (!passCancellation.IsCancellationRequested)
                    await _changed.WaitAsync(index is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { settings.Changed -= Wake; }
    }

    private async Task RunPassAsync(CancellationToken token)
    {
        if (index is not null) {
            await RunIndexedStreamAsync(token);
            if (!index.HasRunnableSmall(thumbnails.Signature)) await RunIndexedStreamAsync(token, medium: true);
            else Volatile.Write(ref _status, "Small thumbnails ready to retry · medium background work paused");
            return;
        }
        List<UserRoot> roots;
        using (var scope = scopes.CreateScope())
            roots = await scope.ServiceProvider.GetRequiredService<GalleryDbContext>().UserRoots
                .AsNoTracking().OrderBy(root => root.Id).ToListAsync(token);
        long completed = 0, failed = 0;
        Volatile.Write(ref _status, $"Scanning {roots.Count} roots");
        await Parallel.ForEachAsync(EnumerateRoots(roots, token), new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, settings.EffectiveBackgroundWorkers), CancellationToken = token
        }, async (entry, cancellation) =>
        {
            try
            {
                // Re-resolve against current ownership/path assignments, including link containment.
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
                var currentRoot = await db.UserRoots.AsNoTracking().SingleOrDefaultAsync(root => root.Id == entry.Root.Id, cancellation);
                if (currentRoot is null || currentRoot.OwnerUserId != entry.Root.OwnerUserId
                    || !FileSystemService.PathsEqual(currentRoot.PhysicalPath, entry.Root.PhysicalPath)) return;
                var relative = Path.GetRelativePath(entry.Root.PhysicalPath, entry.Path);
                var files = scope.ServiceProvider.GetRequiredService<FileSystemService>();
                var resolved = files.ResolvePath(new ApplicationUser { Id = entry.Root.OwnerUserId },
                    FileSystemService.RootMarker(entry.Root.Id) + "/" + relative.Replace(Path.DirectorySeparatorChar, '/'));
                if (FileSystemService.IsIgnoredFileSystemEntry(resolved) || FileSystemService.IsReparsePoint(resolved)) return;
                while (true)
                {
                    try
                    {
                        await thumbnails.GetOrCreateAsync(entry.Root.OwnerUserId, resolved, ThumbnailPriority.Background, cancellation);
                        break;
                    }
                    catch (ThumbnailQueueFullException) { await Task.Delay(500, cancellation); }
                }
                Interlocked.Increment(ref completed);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failed);
                logger.LogDebug(exception, "Skipping background thumbnail for root {RootId}", entry.Root.Id);
            }
            Volatile.Write(ref _status, $"Scanning: {Interlocked.Read(ref completed)} ready, {Interlocked.Read(ref failed)} skipped");
        });
        Volatile.Write(ref _status, $"Pass complete: {completed} ready, {failed} skipped. Next scan in 10 minutes.");
    }

    private async Task RunIndexedStreamAsync(CancellationToken token, bool medium = false)
    {
        var currentIndex = index ?? throw new InvalidOperationException("Index is required.");
        var smallSignature = thumbnails.Signature;
        var signature = medium ? thumbnails.MediumSignature : smallSignature;
        var version = signature[..signature.IndexOf('|')];
        var workers = Math.Max(1, settings.EffectiveBackgroundWorkers);
        // Keep a second bounded set preparing paths / waiting in the generation queue /
        // recording readiness. Actual decoding remains capped by the local/remote queue.
        var preparedJobs = Math.Clamp(workers * 2, 2, 32);
        var feed = new IndexedThumbnailFeed(count => smallSignature != thumbnails.Signature ? [] :
            medium ? currentIndex.PendingMedium(smallSignature, signature, count) : currentIndex.Pending(signature, count), preparedJobs);
        long completed = 0, failed = 0;
        await Parallel.ForEachAsync(feed.ReadAsync(token), new ParallelOptions
        {
            MaxDegreeOfParallelism = preparedJobs, CancellationToken = token
        }, async (entry, cancellation) =>
        {
            try
            {
                if (medium && currentIndex.HasRunnableSmall(smallSignature)) return;
                using var scope = scopes.CreateScope();
                var files = scope.ServiceProvider.GetRequiredService<FileSystemService>();
                var path = files.ResolvePath(new ApplicationUser { Id = entry.OwnerId },
                    FileSystemService.RootMarker(entry.RootId) + "/" + entry.RelativePath);
                var info = new FileInfo(path);
                if (!info.Exists || FileSystemService.IsIgnoredFileSystemEntry(path) || FileSystemService.IsReparsePoint(path)
                    || !FileSystemService.PathsEqual(path, entry.PhysicalKey) || info.Length != entry.Size || info.LastWriteTimeUtc.Ticks != entry.ModifiedTicks)
                {
                    currentIndex.RefreshAfterThumbnailFailure(entry, medium);
                    return;
                }
                while (true)
                {
                    try { await thumbnails.GetOrCreateAsync(entry.OwnerId, path, ThumbnailPriority.Background, cancellation, version, medium); break; }
                    catch (ThumbnailQueueFullException) { await Task.Delay(500, cancellation); }
                }
                // Same fingerprint condition prevents completion of an old job marking an updated file ready.
                if (smallSignature == thumbnails.Signature) {
                    if (medium) currentIndex.RecordMediumThumbnail(entry.OwnerId, path, entry.Size, entry.ModifiedTicks, signature);
                    else currentIndex.RecordThumbnail(entry.OwnerId, path, entry.Size, entry.ModifiedTicks, signature);
                }
                Interlocked.Increment(ref completed);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (SmallThumbnailsPendingException) { /* Return to small work without deferring medium for an hour. */ }
            catch (BackgroundThumbnailPreemptedException) { /* On-demand owns the slots; retry via the feed without failure backoff. */ }
            catch (Exception exception)
            {
                currentIndex.RefreshAfterThumbnailFailure(entry, medium);
                Interlocked.Increment(ref failed);
                logger.LogDebug(exception, "Skipping indexed thumbnail for root {RootId}", entry.RootId);
            }
            finally { feed.Complete(entry); }
            Volatile.Write(ref _status, $"Processing {(medium ? "medium" : "small")} thumbnails: {Interlocked.Read(ref completed)} ready, {Interlocked.Read(ref failed)} deferred");
        });
        Volatile.Write(ref _status, $"Waiting for {(medium ? "medium" : "small")} thumbnails · {completed} ready, {failed} deferred");
    }

    private IEnumerable<(UserRoot Root, string Path)> EnumerateRoots(IEnumerable<UserRoot> roots, CancellationToken token)
    {
        foreach (var root in roots)
            foreach (var path in EnumerateImages(root.PhysicalPath, token, thumbnails.CachePath)) yield return (root, path);
    }

    public static IEnumerable<string> EnumerateImages(string root, CancellationToken token, string? excludedDirectory = null)
    {
        bool Excluded(string path) => excludedDirectory is not null && (FileSystemService.PathsEqual(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(excludedDirectory))
            || path.StartsWith(Path.TrimEndingDirectorySeparator(excludedDirectory) + Path.DirectorySeparatorChar, FileSystemService.PathComparison));
        if (Excluded(root) || !Directory.Exists(root) || FileSystemService.IsIgnoredFileSystemEntry(root) || FileSystemService.IsReparsePoint(root)) yield break;
        // One enumerator per depth, not a materialized list of the whole media tree.
        var pending = new Stack<IEnumerator<string>>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System };
        pending.Push(Directory.EnumerateFileSystemEntries(root, "*", options).GetEnumerator());
        try
        {
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var iterator = pending.Peek();
                bool moved;
                try { moved = iterator.MoveNext(); }
                catch (IOException) { moved = false; }
                catch (UnauthorizedAccessException) { moved = false; }
                if (!moved) { pending.Pop().Dispose(); continue; }
                var path = iterator.Current;
                if (Excluded(path) || FileSystemService.IsIgnoredFileSystemEntry(path) || FileSystemService.IsReparsePoint(path)) continue;
                if (Directory.Exists(path)) pending.Push(Directory.EnumerateFileSystemEntries(path, "*", options).GetEnumerator());
                else if (FileSystemService.HasThumbnail(Path.GetExtension(path))) yield return path;
            }
        }
        finally { foreach (var iterator in pending) iterator.Dispose(); }
    }
}
