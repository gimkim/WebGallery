using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace WebGallery.Services;

public sealed class LoginAttemptLimiter : IDisposable
{
    public static readonly TimeSpan UserWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan IpWindow = TimeSpan.FromMinutes(5);
    public const int UserFailureLimit = 5;
    public const int IpFailureLimit = 12;

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private readonly Lock cacheLock = new();
    private readonly TimeProvider timeProvider;

    public LoginAttemptLimiter(TimeProvider timeProvider) => this.timeProvider = timeProvider;

    public LoginCooldown GetCooldown(string clientAddress, string userName)
    {
        var now = timeProvider.GetUtcNow();
        var userCooldown = GetBucketCooldown(UserKey(userName), UserFailureLimit, UserWindow, now);
        var ipCooldown = GetBucketCooldown(IpKey(clientAddress), IpFailureLimit, IpWindow, now);
        return LoginCooldown.Longest(userCooldown, ipCooldown);
    }

    public LoginCooldown RecordFailure(string clientAddress, string userName)
    {
        var now = timeProvider.GetUtcNow();
        var userCooldown = RecordFailure(UserKey(userName), UserFailureLimit, UserWindow, now);
        var ipCooldown = RecordFailure(IpKey(clientAddress), IpFailureLimit, IpWindow, now);
        return LoginCooldown.Longest(userCooldown, ipCooldown);
    }

    public void Reset(string clientAddress, string userName)
    {
        cache.Remove(UserKey(userName));
        cache.Remove(IpKey(clientAddress));
    }

    public void Dispose() => cache.Dispose();

    private LoginCooldown GetBucketCooldown(string key, int limit, TimeSpan window, DateTimeOffset now)
    {
        if (!cache.TryGetValue<AttemptBucket>(key, out var bucket) || bucket is null) return LoginCooldown.None;
        lock (bucket.SyncRoot)
        {
            Prune(bucket, now - window);
            return CreateCooldown(bucket, limit, window, now);
        }
    }

    private LoginCooldown RecordFailure(string key, int limit, TimeSpan window, DateTimeOffset now)
    {
        var bucket = GetOrCreateBucket(key, window);
        lock (bucket.SyncRoot)
        {
            Prune(bucket, now - window);
            bucket.Failures.Enqueue(now);
            return CreateCooldown(bucket, limit, window, now);
        }
    }

    private AttemptBucket GetOrCreateBucket(string key, TimeSpan window)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue<AttemptBucket>(key, out var existing) && existing is not null) return existing;
            var bucket = new AttemptBucket();
            cache.Set(key, bucket, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = window + TimeSpan.FromMinutes(1)
            });
            return bucket;
        }
    }

    private static LoginCooldown CreateCooldown(AttemptBucket bucket, int limit, TimeSpan window, DateTimeOffset now)
    {
        if (bucket.Failures.Count < limit) return LoginCooldown.None;
        var retryAfter = bucket.Failures.Peek() + window - now;
        return retryAfter > TimeSpan.Zero ? new LoginCooldown(retryAfter) : LoginCooldown.None;
    }

    private static void Prune(AttemptBucket bucket, DateTimeOffset threshold)
    {
        while (bucket.Failures.TryPeek(out var failure) && failure <= threshold) bucket.Failures.Dequeue();
    }

    private static string UserKey(string userName) => "user:" + Hash(userName.Trim().ToUpperInvariant());
    private static string IpKey(string clientAddress) => "ip:" + Hash(clientAddress.Trim().ToUpperInvariant());
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class AttemptBucket
    {
        public Lock SyncRoot { get; } = new();
        public Queue<DateTimeOffset> Failures { get; } = new();
    }
}

public readonly record struct LoginCooldown(TimeSpan RetryAfter)
{
    public static LoginCooldown None => new(TimeSpan.Zero);
    public bool IsActive => RetryAfter > TimeSpan.Zero;
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));

    public static LoginCooldown Longest(LoginCooldown first, LoginCooldown second) =>
        first.RetryAfter >= second.RetryAfter ? first : second;
}
