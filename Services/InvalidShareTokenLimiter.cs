using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace WebGallery.Services;

public sealed class InvalidShareTokenLimiter : IDisposable
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    public const int FailureLimit = 20;

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private readonly Lock cacheLock = new();
    private readonly TimeProvider timeProvider;

    public InvalidShareTokenLimiter(TimeProvider timeProvider) => this.timeProvider = timeProvider;

    public ShareTokenCooldown GetCooldown(string clientAddress)
    {
        var key = Key(clientAddress);
        if (!cache.TryGetValue<AttemptBucket>(key, out var bucket) || bucket is null) return ShareTokenCooldown.None;
        var now = timeProvider.GetUtcNow();
        lock (bucket.SyncRoot)
        {
            Prune(bucket, now - Window);
            return CreateCooldown(bucket, now);
        }
    }

    public ShareTokenCooldown RecordInvalidToken(string clientAddress)
    {
        var bucket = GetOrCreateBucket(Key(clientAddress));
        var now = timeProvider.GetUtcNow();
        lock (bucket.SyncRoot)
        {
            Prune(bucket, now - Window);
            bucket.Failures.Enqueue(now);
            return CreateCooldown(bucket, now);
        }
    }

    public void Dispose() => cache.Dispose();

    private AttemptBucket GetOrCreateBucket(string key)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue<AttemptBucket>(key, out var existing) && existing is not null) return existing;
            var bucket = new AttemptBucket();
            cache.Set(key, bucket, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = Window + TimeSpan.FromMinutes(1)
            });
            return bucket;
        }
    }

    private static ShareTokenCooldown CreateCooldown(AttemptBucket bucket, DateTimeOffset now)
    {
        if (bucket.Failures.Count < FailureLimit) return ShareTokenCooldown.None;
        var retryAfter = bucket.Failures.Peek() + Window - now;
        return retryAfter > TimeSpan.Zero ? new ShareTokenCooldown(retryAfter) : ShareTokenCooldown.None;
    }

    private static void Prune(AttemptBucket bucket, DateTimeOffset threshold)
    {
        while (bucket.Failures.TryPeek(out var failure) && failure <= threshold) bucket.Failures.Dequeue();
    }

    private static string Key(string clientAddress) =>
        "invalid-share-ip:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientAddress.Trim().ToUpperInvariant())));

    private sealed class AttemptBucket
    {
        public Lock SyncRoot { get; } = new();
        public Queue<DateTimeOffset> Failures { get; } = new();
    }
}

public readonly record struct ShareTokenCooldown(TimeSpan RetryAfter)
{
    public static ShareTokenCooldown None => new(TimeSpan.Zero);
    public bool IsActive => RetryAfter > TimeSpan.Zero;
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));
}
