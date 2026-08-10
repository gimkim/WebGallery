using WebGallery.Services;

var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
using var limiter = new LoginAttemptLimiter(clock);

for (var attempt = 1; attempt < LoginAttemptLimiter.UserFailureLimit; attempt++)
    Assert(!limiter.RecordFailure("198.51.100.10", "missing-user").IsActive, $"user attempt {attempt} should be allowed");

var userCooldown = limiter.RecordFailure("198.51.100.10", "missing-user");
Assert(userCooldown.IsActive, "fifth username failure should start cooldown");
Assert(userCooldown.RetryAfterSeconds == 900, "username cooldown should start at 15 minutes");
Assert(!limiter.GetCooldown("198.51.100.10", "different-user").IsActive, "a different username should remain available before the IP limit");

LoginCooldown ipCooldown = LoginCooldown.None;
for (var attempt = LoginAttemptLimiter.UserFailureLimit + 1; attempt <= LoginAttemptLimiter.IpFailureLimit; attempt++)
    ipCooldown = limiter.RecordFailure("198.51.100.10", $"spray-target-{attempt}");

Assert(ipCooldown.IsActive, "twelfth failure from one IP should start cooldown");
Assert(ipCooldown.RetryAfterSeconds == 300, "IP cooldown should start at 5 minutes");

clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
Assert(!limiter.GetCooldown("198.51.100.10", "different-user").IsActive, "IP cooldown should expire after 5 minutes");
Assert(limiter.GetCooldown("203.0.113.20", "missing-user").IsActive, "username cooldown should remain active across another IP");

clock.Advance(TimeSpan.FromMinutes(10));
Assert(!limiter.GetCooldown("203.0.113.20", "missing-user").IsActive, "username cooldown should expire after 15 minutes");

for (var attempt = 0; attempt < LoginAttemptLimiter.UserFailureLimit; attempt++)
    limiter.RecordFailure("203.0.113.30", "reset-user");
limiter.Reset("203.0.113.30", "reset-user");
Assert(!limiter.GetCooldown("203.0.113.30", "reset-user").IsActive, "successful-login reset should clear username and IP buckets");

Parallel.For(0, LoginAttemptLimiter.UserFailureLimit, _ => limiter.RecordFailure("203.0.113.40", "parallel-user"));
Assert(limiter.GetCooldown("203.0.113.40", "parallel-user").IsActive, "concurrent failures should not lose increments");

using var shareLimiter = new InvalidShareTokenLimiter(clock);
for (var attempt = 1; attempt < InvalidShareTokenLimiter.FailureLimit; attempt++)
    Assert(!shareLimiter.RecordInvalidToken("192.0.2.50").IsActive, $"invalid share attempt {attempt} should remain a 404");
var shareCooldown = shareLimiter.RecordInvalidToken("192.0.2.50");
Assert(shareCooldown.IsActive, "twentieth invalid share token should start cooldown");
Assert(shareCooldown.RetryAfterSeconds == 300, "invalid share cooldown should start at 5 minutes");
Assert(!shareLimiter.GetCooldown("192.0.2.51").IsActive, "share cooldown must be partitioned by IP");
clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
Assert(!shareLimiter.GetCooldown("192.0.2.50").IsActive, "invalid share cooldown should expire after 5 minutes");
Parallel.For(0, InvalidShareTokenLimiter.FailureLimit, _ => shareLimiter.RecordInvalidToken("192.0.2.60"));
Assert(shareLimiter.GetCooldown("192.0.2.60").IsActive, "concurrent invalid share attempts should not lose increments");

Console.WriteLine("Security limiter self-test passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset utcNow = utcNow;
    public override DateTimeOffset GetUtcNow() => utcNow;
    public void Advance(TimeSpan duration) => utcNow += duration;
}
