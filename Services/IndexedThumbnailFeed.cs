using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WebGallery.Models;

namespace WebGallery.Services;

// The async enumerator has one consumer at a time (Parallel.ForEachAsync guarantees this).
// Finished workers refill independently; there is no barrier at a database page boundary.
public sealed class IndexedThumbnailFeed(Func<int, IReadOnlyList<GalleryIndexEntry>> pending, int workers)
{
    private readonly ConcurrentDictionary<(int Root, string Path), byte> _active = new();
    public void Complete(GalleryIndexEntry item) => _active.TryRemove((item.RootId, item.PathKey), out _);

    public async IAsyncEnumerable<GalleryIndexEntry> ReadAsync([EnumeratorCancellation] CancellationToken token)
    {
        Task<IReadOnlyList<GalleryIndexEntry>> Fetch(HashSet<(int Root, string Path)> excluded) => Task.Run<IReadOnlyList<GalleryIndexEntry>>(() => {
            token.ThrowIfCancellationRequested();
            return pending(64 + excluded.Count + Math.Clamp(workers, 1, 32))
                .Where(item => !excluded.Contains((item.RootId, item.PathKey))).Take(64).ToList();
        }, token);
        Task<IReadOnlyList<GalleryIndexEntry>>? next = Fetch(_active.Keys.ToHashSet());
        try { while (true) {
            token.ThrowIfCancellationRequested();
            var page = await (next ??= Fetch(_active.Keys.ToHashSet()));
            // Read one bounded page ahead while the current page is being processed.
            // Exclude the entire current page, including not-yet-dispatched items: otherwise
            // completion during prefetch could make stale query results run twice.
            var excluded = _active.Keys.Concat(page.Select(item => (item.RootId, item.PathKey))).ToHashSet();
            next = page.Count > 0 ? Fetch(excluded) : null;
            var yielded = false;
            foreach (var item in page)
            {
                token.ThrowIfCancellationRequested();
                if (!_active.TryAdd((item.RootId, item.PathKey), 0)) continue;
                yielded = true;
                yield return item;
            }
            if (yielded) continue;
            if (_active.IsEmpty) yield break;
            await Task.Delay(50, token);
            next ??= Fetch(_active.Keys.ToHashSet());
        } } finally {
            if (next is not null) { try { await next; } catch (OperationCanceledException) when(token.IsCancellationRequested) {} }
        }
    }
}
