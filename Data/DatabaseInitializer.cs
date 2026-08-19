using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebGallery.Models;
using WebGallery.Services;

namespace WebGallery.Data;

public static class DatabaseInitializer
{
    public const string AdminRole = "Admin";

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        await db.Database.EnsureCreatedAsync();
        await EnsureShareLinkPresentationColumnsAsync(db);
        await EnsureCollectionSchemaAsync(db);
        await EnsureShareAuditSchemaAsync(db);

        await db.FolderRules
            .Where(rule => rule.AccessMode != FolderAccessMode.Private)
            .ExecuteUpdateAsync(update => update.SetProperty(rule => rule.AccessMode, FolderAccessMode.Private));

        if (!await db.AppSettings.AnyAsync(x => x.Key == "AppTitle"))
        {
            db.AppSettings.Add(new AppSetting { Key = "AppTitle", Value = configuration["Gallery:AppTitle"] ?? "Gallery" });
            await db.SaveChangesAsync();
        }

        if (!await db.AppSettings.AnyAsync(x => x.Key == "Theme"))
        {
            db.AppSettings.Add(new AppSetting { Key = "Theme", Value = "retro" });
            await db.SaveChangesAsync();
        }

        var options = scope.ServiceProvider.GetRequiredService<IOptions<GalleryOptions>>().Value;
        var loginSecuritySettings = scope.ServiceProvider.GetRequiredService<LoginSecuritySettings>();
        await LoadLoginSecuritySettingsAsync(db, loginSecuritySettings);
        var queueSettings = scope.ServiceProvider.GetRequiredService<ThumbnailQueueSettings>();
        var concurrencySetting = await db.AppSettings.FindAsync("ThumbnailConcurrency");
        var concurrency = ThumbnailQueueSettings.Clamp(options.ThumbnailConcurrency);
        if (concurrencySetting is null)
        {
            concurrencySetting = new AppSetting { Key = "ThumbnailConcurrency", Value = concurrency.ToString() };
            db.AppSettings.Add(concurrencySetting);
            await db.SaveChangesAsync();
        }
        else if (!int.TryParse(concurrencySetting.Value, out concurrency))
        {
            concurrency = ThumbnailQueueSettings.Clamp(options.ThumbnailConcurrency);
            concurrencySetting.Value = concurrency.ToString();
            await db.SaveChangesAsync();
        }
        var normalizedConcurrency = ThumbnailQueueSettings.Clamp(concurrency);
        if (normalizedConcurrency != concurrency)
        {
            concurrency = normalizedConcurrency;
            concurrencySetting.Value = concurrency.ToString();
            await db.SaveChangesAsync();
        }
        queueSettings.Update(concurrency);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(AdminRole))
            await roleManager.CreateAsync(new IdentityRole(AdminRole));

        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var rootPath = Path.GetFullPath(options.DefaultRootPath, environment.ContentRootPath);
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.GetFullPath(options.CachePath, environment.ContentRootPath));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userName = configuration["BootstrapAdmin:UserName"] ?? "admin";
        var admin = await userManager.FindByNameAsync(userName);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = userName,
                DisplayName = configuration["BootstrapAdmin:DisplayName"] ?? "Administrator",
                RootFolder = rootPath
            };
            var password = configuration["BootstrapAdmin:Password"];
            var generatedPassword = string.IsNullOrWhiteSpace(password);
            if (generatedPassword) password = $"Gg1!{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12))}";
            var result = await userManager.CreateAsync(admin, password!);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            if (generatedPassword)
            {
                var credentialFile = Path.Combine(Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!, "bootstrap-admin.txt");
                await File.WriteAllTextAsync(credentialFile, $"UserName: {userName}{Environment.NewLine}Password: {password}{Environment.NewLine}Created: {DateTimeOffset.Now:O}{Environment.NewLine}");
            }
        }
        if (!await userManager.IsInRoleAsync(admin, AdminRole))
            await userManager.AddToRoleAsync(admin, AdminRole);
    }

    private static async Task LoadLoginSecuritySettingsAsync(GalleryDbContext db, LoginSecuritySettings runtimeSettings)
    {
        var defaults = LoginSecurityOptions.Default;
        var values = new Dictionary<string, int>
        {
            ["LoginDelayAfterFailures"] = defaults.DelayAfterFailures,
            ["LoginDelayIncrementSeconds"] = defaults.DelayIncrementSeconds,
            ["LoginUserFailureLimit"] = defaults.UserFailureLimit,
            ["LoginUserCooldownMinutes"] = (int)defaults.UserCooldown.TotalMinutes,
            ["LoginIpFailureLimit"] = defaults.IpFailureLimit,
            ["LoginIpCooldownMinutes"] = (int)defaults.IpCooldown.TotalMinutes
        };
        var changed = false;
        foreach (var key in values.Keys.ToList())
        {
            var setting = await db.AppSettings.FindAsync(key);
            if (setting is null)
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = values[key].ToString() });
                changed = true;
            }
            else if (int.TryParse(setting.Value, out var parsed))
            {
                values[key] = parsed;
            }
            else
            {
                setting.Value = values[key].ToString();
                changed = true;
            }
        }

        if (!LoginSecuritySettings.TryCreate(
            values["LoginDelayAfterFailures"],
            values["LoginDelayIncrementSeconds"],
            values["LoginUserFailureLimit"],
            values["LoginUserCooldownMinutes"],
            values["LoginIpFailureLimit"],
            values["LoginIpCooldownMinutes"],
            out var options,
            out _))
        {
            options = defaults;
            foreach (var pair in new Dictionary<string, int>
            {
                ["LoginDelayAfterFailures"] = defaults.DelayAfterFailures,
                ["LoginDelayIncrementSeconds"] = defaults.DelayIncrementSeconds,
                ["LoginUserFailureLimit"] = defaults.UserFailureLimit,
                ["LoginUserCooldownMinutes"] = (int)defaults.UserCooldown.TotalMinutes,
                ["LoginIpFailureLimit"] = defaults.IpFailureLimit,
                ["LoginIpCooldownMinutes"] = (int)defaults.IpCooldown.TotalMinutes
            })
            {
                var setting = await db.AppSettings.FindAsync(pair.Key);
                if (setting is null) db.AppSettings.Add(new AppSetting { Key = pair.Key, Value = pair.Value.ToString() });
                else setting.Value = pair.Value.ToString();
            }
            changed = true;
        }
        if (changed) await db.SaveChangesAsync();
        runtimeSettings.Update(options);
    }

    private static async Task EnsureShareLinkPresentationColumnsAsync(GalleryDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('ShareLinks')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }

            var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sort"] = "ALTER TABLE ShareLinks ADD COLUMN Sort TEXT NOT NULL DEFAULT 'name'",
                ["Direction"] = "ALTER TABLE ShareLinks ADD COLUMN Direction TEXT NOT NULL DEFAULT 'asc'",
                ["ItemsPerRow"] = "ALTER TABLE ShareLinks ADD COLUMN ItemsPerRow INTEGER NOT NULL DEFAULT 8",
                ["ViewMode"] = "ALTER TABLE ShareLinks ADD COLUMN ViewMode TEXT NOT NULL DEFAULT 'grid'"
            };
            foreach (var addition in additions.Where(addition => !columns.Contains(addition.Key)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = addition.Value;
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task EnsureCollectionSchemaAsync(GalleryDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS Collections (
                        Id INTEGER NOT NULL CONSTRAINT PK_Collections PRIMARY KEY AUTOINCREMENT,
                        OwnerUserId TEXT NOT NULL,
                        Name TEXT COLLATE NOCASE NOT NULL,
                        CreatedAtUtc TEXT NOT NULL,
                        CONSTRAINT FK_Collections_AspNetUsers_OwnerUserId FOREIGN KEY (OwnerUserId) REFERENCES AspNetUsers (Id) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_Collections_OwnerUserId_Name ON Collections (OwnerUserId, Name);
                    CREATE TABLE IF NOT EXISTS CollectionFolders (
                        Id INTEGER NOT NULL CONSTRAINT PK_CollectionFolders PRIMARY KEY AUTOINCREMENT,
                        CollectionId INTEGER NOT NULL,
                        RelativePath TEXT COLLATE NOCASE NOT NULL,
                        AddedAtUtc TEXT NOT NULL,
                        CONSTRAINT FK_CollectionFolders_Collections_CollectionId FOREIGN KEY (CollectionId) REFERENCES Collections (Id) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_CollectionFolders_CollectionId_RelativePath ON CollectionFolders (CollectionId, RelativePath);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('ShareLinks')";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("CollectionId"))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE ShareLinks ADD COLUMN CollectionId INTEGER NULL";
                await command.ExecuteNonQueryAsync();
            }
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE INDEX IF NOT EXISTS IX_ShareLinks_CollectionId ON ShareLinks (CollectionId)";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task EnsureShareAuditSchemaAsync(GalleryDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ShareAuditEvents (
                    Id INTEGER NOT NULL CONSTRAINT PK_ShareAuditEvents PRIMARY KEY AUTOINCREMENT,
                    ShareLinkId INTEGER NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    OccurredAtUnixSeconds INTEGER NOT NULL DEFAULT 0,
                    EventType TEXT NOT NULL,
                    TargetPath TEXT NOT NULL DEFAULT '',
                    Details TEXT NOT NULL DEFAULT '',
                    ItemCount INTEGER NOT NULL DEFAULT 1,
                    ClientIp TEXT NOT NULL DEFAULT 'unknown',
                    VisitorHash TEXT NOT NULL,
                    CONSTRAINT FK_ShareAuditEvents_ShareLinks_ShareLinkId FOREIGN KEY (ShareLinkId) REFERENCES ShareLinks (Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ShareAuditEvents_ShareLinkId_OccurredAtUtc ON ShareAuditEvents (ShareLinkId, OccurredAtUtc);
                CREATE INDEX IF NOT EXISTS IX_ShareAuditEvents_ShareLinkId_EventType ON ShareAuditEvents (ShareLinkId, EventType);
                """;
            await command.ExecuteNonQueryAsync();

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var columnCommand = connection.CreateCommand())
            {
                columnCommand.CommandText = "PRAGMA table_info('ShareAuditEvents')";
                await using var reader = await columnCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("ClientIp"))
            {
                await using var addColumnCommand = connection.CreateCommand();
                addColumnCommand.CommandText = "ALTER TABLE ShareAuditEvents ADD COLUMN ClientIp TEXT NOT NULL DEFAULT 'unknown'";
                await addColumnCommand.ExecuteNonQueryAsync();
            }
            if (!columns.Contains("OccurredAtUnixSeconds"))
            {
                await using var addColumnCommand = connection.CreateCommand();
                addColumnCommand.CommandText = "ALTER TABLE ShareAuditEvents ADD COLUMN OccurredAtUnixSeconds INTEGER NOT NULL DEFAULT 0";
                await addColumnCommand.ExecuteNonQueryAsync();
            }

            await using var backfillCommand = connection.CreateCommand();
            backfillCommand.CommandText = "UPDATE ShareAuditEvents SET OccurredAtUnixSeconds = CAST(strftime('%s', OccurredAtUtc) AS INTEGER) WHERE OccurredAtUnixSeconds = 0";
            await backfillCommand.ExecuteNonQueryAsync();

            await using var indexCommand = connection.CreateCommand();
            indexCommand.CommandText = """
                CREATE INDEX IF NOT EXISTS IX_ShareAuditEvents_OccurredAtUnixSeconds ON ShareAuditEvents (OccurredAtUnixSeconds);
                CREATE INDEX IF NOT EXISTS IX_ShareAuditEvents_EventType_OccurredAtUnixSeconds ON ShareAuditEvents (EventType, OccurredAtUnixSeconds);
                CREATE INDEX IF NOT EXISTS IX_ShareAuditEvents_ClientIp_OccurredAtUnixSeconds ON ShareAuditEvents (ClientIp, OccurredAtUnixSeconds);
                """;
            await indexCommand.ExecuteNonQueryAsync();
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
