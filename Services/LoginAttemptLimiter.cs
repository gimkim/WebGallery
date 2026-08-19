using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace WebGallery.Services;

public sealed class LoginAttemptLimiter : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private readonly Lock cacheLock = new();
    private readonly TimeProvider timeProvider;
    private readonly LoginSecuritySettings settings;

    public LoginAttemptLimiter(TimeProvider timeProvider, LoginSecuritySettings settings)
    {
        this.timeProvider = timeProvider;
        this.settings = settings;
    }

    public LoginCooldown GetCooldown(string clientAddress, string userName)
    {
        var now = timeProvider.GetUtcNow();
        var options = settings.Current;
        var userCooldown = GetBucketCooldown(UserKey(userName), options.UserFailureLimit, options.UserCooldown, options, true, now);
        var ipCooldown = GetBucketCooldown(IpKey(clientAddress), options.IpFailureLimit, options.IpCooldown, options, false, now);
        return LoginCooldown.Longest(userCooldown, ipCooldown);
    }

    public LoginCooldown RecordFailure(string clientAddress, string userName)
    {
        var now = timeProvider.GetUtcNow();
        var options = settings.Current;
        var userCooldown = RecordFailure(UserKey(userName), options.UserFailureLimit, options.UserCooldown, options, true, now);
        var ipCooldown = RecordFailure(IpKey(clientAddress), options.IpFailureLimit, options.IpCooldown, options, false, now);
        return LoginCooldown.Longest(userCooldown, ipCooldown);
    }

    public void Reset(string clientAddress, string userName)
    {
        cache.Remove(UserKey(userName));
        cache.Remove(IpKey(clientAddress));
    }

    public void Dispose() => cache.Dispose();

    private LoginCooldown GetBucketCooldown(string key, int limit, TimeSpan window, LoginSecurityOptions options, bool progressiveDelay, DateTimeOffset now)
    {
        if (!cache.TryGetValue<AttemptBucket>(key, out var bucket) || bucket is null) return LoginCooldown.None;
        lock (bucket.SyncRoot)
        {
            Prune(bucket, now - window);
            return CreateCooldown(bucket, limit, window, options, progressiveDelay, now);
        }
    }

    private LoginCooldown RecordFailure(string key, int limit, TimeSpan window, LoginSecurityOptions options, bool progressiveDelay, DateTimeOffset now)
    {
        var bucket = GetOrCreateBucket(key);
        lock (bucket.SyncRoot)
        {
            Prune(bucket, now - window);
            bucket.Failures.Enqueue(now);
            return CreateCooldown(bucket, limit, window, options, progressiveDelay, now);
        }
    }

    private AttemptBucket GetOrCreateBucket(string key)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue<AttemptBucket>(key, out var existing) && existing is not null) return existing;
            var bucket = new AttemptBucket();
            cache.Set(key, bucket, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromMinutes(LoginSecuritySettings.MaximumCooldownMinutes + 1)
            });
            return bucket;
        }
    }

    private static LoginCooldown CreateCooldown(AttemptBucket bucket, int limit, TimeSpan window, LoginSecurityOptions options, bool progressiveDelay, DateTimeOffset now)
    {
        if (bucket.CooldownStartedAt is DateTimeOffset cooldownStartedAt)
        {
            var activeRetryAfter = cooldownStartedAt + window - now;
            if (activeRetryAfter > TimeSpan.Zero) return new LoginCooldown(activeRetryAfter);
            bucket.CooldownStartedAt = null;
            bucket.Failures.Clear();
        }
        if (bucket.Failures.Count >= limit)
        {
            bucket.CooldownStartedAt = bucket.Failures.Last();
            return new LoginCooldown(window);
        }
        if (!progressiveDelay || options.DelayIncrementSeconds == 0 || bucket.Failures.Count < options.DelayAfterFailures)
            return LoginCooldown.None;
        var delaySteps = bucket.Failures.Count - options.DelayAfterFailures + 1;
        var delayUntil = bucket.Failures.Last() + TimeSpan.FromSeconds((long)options.DelayIncrementSeconds * delaySteps);
        var delay = delayUntil - now;
        return delay > TimeSpan.Zero ? new LoginCooldown(delay) : LoginCooldown.None;
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
        public DateTimeOffset? CooldownStartedAt { get; set; }
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
