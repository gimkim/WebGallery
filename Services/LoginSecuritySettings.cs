namespace WebGallery.Services;

public sealed class LoginSecuritySettings
{
    public const int DefaultDelayAfterFailures = 3;
    public const int DefaultDelayIncrementSeconds = 2;
    public const int DefaultUserFailureLimit = 5;
    public const int DefaultUserCooldownMinutes = 15;
    public const int DefaultIpFailureLimit = 12;
    public const int DefaultIpCooldownMinutes = 5;

    public const int MinimumFailureLimit = 2;
    public const int MaximumFailureLimit = 100;
    public const int MinimumDelayAfterFailures = 1;
    public const int MaximumDelayIncrementSeconds = 300;
    public const int MinimumCooldownMinutes = 1;
    public const int MaximumCooldownMinutes = 1440;

    private LoginSecurityOptions current = LoginSecurityOptions.Default;

    public LoginSecurityOptions Current => Volatile.Read(ref current);

    public void Update(LoginSecurityOptions options) => Volatile.Write(ref current, options);

    public static bool TryCreate(
        int delayAfterFailures,
        int delayIncrementSeconds,
        int userFailureLimit,
        int userCooldownMinutes,
        int ipFailureLimit,
        int ipCooldownMinutes,
        out LoginSecurityOptions options,
        out string error)
    {
        options = LoginSecurityOptions.Default;
        if (delayAfterFailures is < MinimumDelayAfterFailures or > MaximumFailureLimit)
            return Fail($"Delay start must be between {MinimumDelayAfterFailures} and {MaximumFailureLimit} failed attempts.", out error);
        if (delayIncrementSeconds is < 0 or > MaximumDelayIncrementSeconds)
            return Fail($"Delay increment must be between 0 and {MaximumDelayIncrementSeconds} seconds.", out error);
        if (userFailureLimit is < MinimumFailureLimit or > MaximumFailureLimit)
            return Fail($"Username cooldown threshold must be between {MinimumFailureLimit} and {MaximumFailureLimit} failures.", out error);
        if (delayAfterFailures >= userFailureLimit)
            return Fail("Delay must start before the username cooldown threshold.", out error);
        if (userCooldownMinutes is < MinimumCooldownMinutes or > MaximumCooldownMinutes)
            return Fail($"Username cooldown must be between {MinimumCooldownMinutes} and {MaximumCooldownMinutes} minutes.", out error);
        if (ipFailureLimit is < MinimumFailureLimit or > MaximumFailureLimit)
            return Fail($"IP cooldown threshold must be between {MinimumFailureLimit} and {MaximumFailureLimit} failures.", out error);
        if (ipCooldownMinutes is < MinimumCooldownMinutes or > MaximumCooldownMinutes)
            return Fail($"IP cooldown must be between {MinimumCooldownMinutes} and {MaximumCooldownMinutes} minutes.", out error);

        options = new LoginSecurityOptions(
            delayAfterFailures,
            delayIncrementSeconds,
            userFailureLimit,
            TimeSpan.FromMinutes(userCooldownMinutes),
            ipFailureLimit,
            TimeSpan.FromMinutes(ipCooldownMinutes));
        error = "";
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}

public sealed record LoginSecurityOptions(
    int DelayAfterFailures,
    int DelayIncrementSeconds,
    int UserFailureLimit,
    TimeSpan UserCooldown,
    int IpFailureLimit,
    TimeSpan IpCooldown)
{
    public static LoginSecurityOptions Default { get; } = new(
        LoginSecuritySettings.DefaultDelayAfterFailures,
        LoginSecuritySettings.DefaultDelayIncrementSeconds,
        LoginSecuritySettings.DefaultUserFailureLimit,
        TimeSpan.FromMinutes(LoginSecuritySettings.DefaultUserCooldownMinutes),
        LoginSecuritySettings.DefaultIpFailureLimit,
        TimeSpan.FromMinutes(LoginSecuritySettings.DefaultIpCooldownMinutes));
}
