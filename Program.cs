using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Net;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;

var builder = WebApplication.CreateBuilder(args);

var hostingOptions = builder.Configuration.GetSection("Hosting").Get<HostingOptions>() ?? new HostingOptions();
var reverseProxyOptions = builder.Configuration.GetSection("ReverseProxy").Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();

var connectionString = builder.Configuration.GetConnectionString("Gallery")
    ?? throw new InvalidOperationException("Connection string 'Gallery' is not configured.");
var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
var databasePath = Path.GetFullPath(dataSource, builder.Environment.ContentRootPath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
var dataProtectionKeysPath = Path.GetFullPath(
    builder.Configuration["Gallery:DataProtectionKeysPath"] ?? "App_Data/keys",
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("WebGallery");

builder.Services.AddDbContext<GalleryDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        // AccountController applies the database-backed runtime threshold and duration.
        options.Lockout.MaxFailedAccessAttempts = int.MaxValue;
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<GalleryDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.Cookie.Name = "GimGallery.Auth";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
});
builder.Services.Configure<GalleryOptions>(builder.Configuration.GetSection("Gallery"));
if (reverseProxyOptions.Enabled)
{
    var trustedProxies = reverseProxyOptions.KnownProxies
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(ParseProxyAddress)
        .ToArray();
    var trustedNetworks = reverseProxyOptions.KnownNetworks
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(ParseProxyNetwork)
        .ToArray();
    var allowedHosts = reverseProxyOptions.AllowedHosts
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (allowedHosts.Length == 0)
        throw new InvalidOperationException("ReverseProxy:AllowedHosts must contain the public host names when forwarded headers are enabled.");
    if (allowedHosts.Any(host => host is "*" or "[::]" or "0.0.0.0"))
        throw new InvalidOperationException("ReverseProxy:AllowedHosts must list explicit public host names; wildcard hosts are not accepted.");

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost
            | ForwardedHeaders.XForwardedPrefix;
        options.ForwardLimit = Math.Clamp(reverseProxyOptions.ForwardLimit, 1, 5);
        foreach (var address in trustedProxies)
        {
            options.KnownProxies.Add(address);
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                options.KnownProxies.Add(address.MapToIPv6());
        }
        foreach (var network in trustedNetworks) options.KnownIPNetworks.Add(network);
        foreach (var host in allowedHosts) options.AllowedHosts.Add(host);
    });
}
builder.Services.AddControllersWithViews(options => options.Filters.Add<PageApiFilter>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FileSystemService>();
builder.Services.AddSingleton<UploadSessions>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<UploadSessions>());
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<ShareAuditService>();
builder.Services.AddSingleton(_ => new ThumbnailQueueSettings(
    builder.Configuration.GetValue("Gallery:ThumbnailConcurrency", ThumbnailQueueSettings.DefaultConcurrency)));
builder.Services.AddSingleton<ThumbnailWorkQueue>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<ThumbnailWorkQueue>());
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<RemoteResizer>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<RemoteResizer>());
builder.Services.AddSingleton<GalleryIndexService>();
builder.Services.AddSingleton<DateTakenIndexer>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<DateTakenIndexer>());
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<GalleryIndexService>());
builder.Services.AddSingleton<BackgroundThumbnailService>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<BackgroundThumbnailService>());
builder.Services.AddSingleton<MediaService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<LoginSecuritySettings>();
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddSingleton<InvalidShareTokenLimiter>();

var app = builder.Build();

if (reverseProxyOptions.Enabled) app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
if (hostingOptions.UseHttpsRedirection) app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) => {
    if (context.User.Identity?.IsAuthenticated == true) {
        var action = context.GetRouteValue("action")?.ToString();
        var account = string.Equals(context.GetRouteValue("controller")?.ToString(), "Account", StringComparison.OrdinalIgnoreCase);
        if (!(account && action is "ChangePassword" or "Logout" or "ResetPassword")) {
            var manager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var current = await manager.GetUserAsync(context.User);
            var stampClaim=context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
            if (current is null || current.SecurityStamp != context.User.FindFirst(stampClaim)?.Value) {
                await context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
                if(context.Request.Path.StartsWithSegments("/Files")) {
                    context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new { error="Your sign-in session ended. Sign in again." });
                } else context.Response.Redirect(context.Request.PathBase+"/Account/Login");
                return;
            }
            if (current.RequirePasswordChange) {
                if (context.Request.Path.StartsWithSegments("/Files")) {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { error="Change your password before continuing." });
                } else context.Response.Redirect(context.Request.PathBase + "/Account/ChangePassword");
                return;
            }
        }
    }
    await next();
});
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Gallery}/{action=Index}/{id?}");
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);
app.Run();

static IPAddress ParseProxyAddress(string value)
{
    if (IPAddress.TryParse(value.Trim(), out var address)) return address;
    throw new InvalidOperationException($"ReverseProxy:KnownProxies contains an invalid IP address: {value}");
}

static System.Net.IPNetwork ParseProxyNetwork(string value)
{
    if (System.Net.IPNetwork.TryParse(value.Trim(), out var network)) return network;
    throw new InvalidOperationException($"ReverseProxy:KnownNetworks contains an invalid CIDR network: {value}");
}

public partial class Program;
