using WebGallery.Services;

var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
var loginSettings = new LoginSecuritySettings();
using var limiter = new LoginAttemptLimiter(clock, loginSettings);

for (var attempt = 1; attempt < LoginSecuritySettings.DefaultDelayAfterFailures; attempt++)
    Assert(!limiter.RecordFailure("198.51.100.10", "missing-user").IsActive, $"user attempt {attempt} should be allowed");

var progressiveDelay = limiter.RecordFailure("198.51.100.10", "missing-user");
Assert(progressiveDelay.RetryAfterSeconds == 2, "third username failure should delay the next retry by 2 seconds");
clock.Advance(TimeSpan.FromSeconds(3));
progressiveDelay = limiter.RecordFailure("198.51.100.10", "missing-user");
Assert(progressiveDelay.RetryAfterSeconds == 4, "fourth username failure should delay the next retry by 4 seconds");
clock.Advance(TimeSpan.FromSeconds(5));
var userCooldown = limiter.RecordFailure("198.51.100.10", "missing-user");
Assert(userCooldown.IsActive, "fifth username failure should start cooldown");
Assert(userCooldown.RetryAfterSeconds == 900, "username cooldown should start at 15 minutes");
Assert(!limiter.GetCooldown("198.51.100.10", "different-user").IsActive, "a different username should remain available before the IP limit");

LoginCooldown ipCooldown = LoginCooldown.None;
for (var attempt = LoginSecuritySettings.DefaultUserFailureLimit + 1; attempt <= LoginSecuritySettings.DefaultIpFailureLimit; attempt++)
    ipCooldown = limiter.RecordFailure("198.51.100.10", $"spray-target-{attempt}");

Assert(ipCooldown.IsActive, "twelfth failure from one IP should start cooldown");
Assert(ipCooldown.RetryAfterSeconds == 300, "IP cooldown should start at 5 minutes");

clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
Assert(!limiter.GetCooldown("198.51.100.10", "different-user").IsActive, "IP cooldown should expire after 5 minutes");
Assert(limiter.GetCooldown("203.0.113.20", "missing-user").IsActive, "username cooldown should remain active across another IP");

clock.Advance(TimeSpan.FromMinutes(10));
Assert(!limiter.GetCooldown("203.0.113.20", "missing-user").IsActive, "username cooldown should expire after 15 minutes");

for (var attempt = 0; attempt < LoginSecuritySettings.DefaultDelayAfterFailures; attempt++)
    limiter.RecordFailure("203.0.113.30", "reset-user");
limiter.Reset("203.0.113.30", "reset-user");
Assert(!limiter.GetCooldown("203.0.113.30", "reset-user").IsActive, "successful-login reset should clear username and IP buckets");

Parallel.For(0, LoginSecuritySettings.DefaultUserFailureLimit, _ => limiter.RecordFailure("203.0.113.40", "parallel-user"));
Assert(limiter.GetCooldown("203.0.113.40", "parallel-user").IsActive, "concurrent failures should not lose increments");

Assert(LoginSecuritySettings.TryCreate(2, 7, 4, 20, 8, 10, out var changedOptions, out _), "valid runtime settings should be accepted");
loginSettings.Update(changedOptions);
using var changedLimiter = new LoginAttemptLimiter(clock, loginSettings);
Assert(!changedLimiter.RecordFailure("203.0.113.50", "configured-user").IsActive, "configured first failure should remain available");
Assert(changedLimiter.RecordFailure("203.0.113.50", "configured-user").RetryAfterSeconds == 7, "configured delay should apply immediately");
clock.Advance(TimeSpan.FromSeconds(8));
changedLimiter.RecordFailure("203.0.113.50", "configured-user");
clock.Advance(TimeSpan.FromSeconds(15));
Assert(changedLimiter.RecordFailure("203.0.113.50", "configured-user").RetryAfterSeconds == 1200, "configured username cooldown should apply immediately");
Assert(!LoginSecuritySettings.TryCreate(5, 2, 5, 15, 12, 5, out _, out _), "delay threshold at the cooldown threshold must be rejected");
Assert(!LoginSecuritySettings.TryCreate(3, 301, 5, 15, 12, 5, out _, out _), "excessive delay increment must be rejected");

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
