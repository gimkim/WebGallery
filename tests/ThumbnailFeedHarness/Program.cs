using System.Collections.Concurrent;
using System.Diagnostics;
using WebGallery.Models;
using WebGallery.Services;

var entries = Enumerable.Range(0, 200).Select(i => new GalleryIndexEntry { RootId = 1, PathKey = i.ToString("D4") }).ToArray();
var done = new ConcurrentDictionary<string, byte>();
var calls = new ConcurrentDictionary<string, int>();
var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var crossedPage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var feed = new IndexedThumbnailFeed(count => entries.Where(x => !done.ContainsKey(x.PathKey)).Take(count).ToList(), 4);
var watch = Stopwatch.StartNew();
var active = 0;
var run = Parallel.ForEachAsync(feed.ReadAsync(default), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (entry, token) =>
{
    if (Interlocked.Increment(ref active) > 4) throw new Exception("Exceeded workers");
    try
    {
        if (calls.AddOrUpdate(entry.PathKey, 1, (_, value) => value + 1) != 1) throw new Exception("Duplicate scheduling");
        if (entry.PathKey == "0000") await releaseSlow.Task.WaitAsync(token);
        else await Task.Delay(1, token);
        if (entry.PathKey == "0150") crossedPage.SetResult();
        done.TryAdd(entry.PathKey, 0);
    }
    finally { Interlocked.Decrement(ref active); feed.Complete(entry); }
});
try
{
    await crossedPage.Task.WaitAsync(TimeSpan.FromSeconds(4));
    if (done.ContainsKey("0000")) throw new Exception("Slow item should still be held");
    Console.WriteLine($"Crossed 150 items in {watch.ElapsedMilliseconds}ms while first item remained blocked: no batch barrier or three-second pause.");
}
finally { releaseSlow.TrySetResult(); }
await run.WaitAsync(TimeSpan.FromSeconds(4));
if (done.Count != 200 || calls.Count != 200) throw new Exception("Lost entries");
using var cancellation = new CancellationTokenSource();
var waiting = new IndexedThumbnailFeed(_ => [entries[0]], 1);
await using var iterator = waiting.ReadAsync(cancellation.Token).GetAsyncEnumerator();
if (!await iterator.MoveNextAsync()) throw new Exception("Expected entry");
var next = iterator.MoveNextAsync().AsTask();
cancellation.Cancel();
try { await next; throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { }
Console.WriteLine("PASS: bounded workers, continuous refill across pages, no duplicates, idle completion, cancellation during in-flight wait.");
// A slow page query must begin while the current page is still being consumed.
var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var fetchRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var fetched = 0;
var prefetched = new IndexedThumbnailFeed(count => {
    if (Interlocked.Increment(ref fetched) == 2) { fetchStarted.SetResult(); fetchRelease.Task.GetAwaiter().GetResult(); }
    return entries.Take(count).ToList();
}, 4);
await using (var prefetchedReader=prefetched.ReadAsync(default).GetAsyncEnumerator()) {
    if(!await prefetchedReader.MoveNextAsync()) throw new Exception("No initial work");
    try {
        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Next SQL query remains blocked, yet the current page can continue.
        for(var i=0;i<20;i++) if(!await prefetchedReader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1))) throw new Exception("Unexpected page end");
        Console.WriteLine("PASS: next-page query overlaps current-page consumption without blocking workers");
    } finally { fetchRelease.TrySetResult(); }
}
