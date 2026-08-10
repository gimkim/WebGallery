using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;

var builder = WebApplication.CreateBuilder(args);

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
        options.Lockout.MaxFailedAccessAttempts = LoginAttemptLimiter.UserFailureLimit;
        options.Lockout.DefaultLockoutTimeSpan = LoginAttemptLimiter.UserWindow;
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
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<FileSystemService>();
builder.Services.AddSingleton(_ => new ThumbnailQueueSettings(
    builder.Configuration.GetValue("Gallery:ThumbnailConcurrency", ThumbnailQueueSettings.DefaultConcurrency)));
builder.Services.AddSingleton<ThumbnailWorkQueue>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<ThumbnailWorkQueue>());
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddSingleton<InvalidShareTokenLimiter>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Gallery}/{action=Index}/{id?}");

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);
app.Run();

public partial class Program;
