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
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS FolderBrowsePreferences (
                OwnerId TEXT NOT NULL REFERENCES AspNetUsers(Id) ON DELETE CASCADE,
                FolderKey TEXT NOT NULL, Sort TEXT NOT NULL, Direction TEXT NOT NULL,
                PRIMARY KEY (OwnerId, FolderKey))
            """);
        await EnsurePasswordColumnAsync(db);
        await EnsureShareLinkPresentationColumnsAsync(db);
        await EnsureCollectionSchemaAsync(db);
        await EnsureShareTargetTypeAsync(db);
        await EnsureUserRootSchemaAsync(db);
        if (await db.UserRoots.GroupBy(x => x.OwnerUserId).AnyAsync(x => x.Count() > 1))
            throw new InvalidOperationException("Single-root migration requires at most one root per user. No roots have been deleted.");
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserRoots_SingleOwner ON UserRoots(OwnerUserId)");
        await GalleryIndexService.EnsureSchemaAsync(db);
        var indexHours = (await db.AppSettings.FindAsync("IndexReconcileHours"))?.Value;
        var indexWorkers = (await db.AppSettings.FindAsync("DateTakenWorkers"))?.Value;
        scope.ServiceProvider.GetService<DateTakenIndexer>()?.SetWorkers(int.TryParse(indexWorkers, out var workerCount) ? workerCount : 4);
        scope.ServiceProvider.GetService<GalleryIndexService>()?.SetReconcileHours(int.TryParse(indexHours, out var hours) ? hours : 24);
        await EnsureShareAuditSchemaAsync(db);

        await db.FolderRules
            .Where(rule => rule.AccessMode != FolderAccessMode.Private)
            .ExecuteUpdateAsync(update => update.SetProperty(rule => rule.AccessMode, FolderAccessMode.Private));

        if (!await db.AppSettings.AnyAsync(x => x.Key == "AppTitle"))
        {
            db.AppSettings.Add(new AppSetting { Key = "AppTitle", Value = configuration["Gallery:AppTitle"] ?? "Gallery" });
            await db.SaveChangesAsync();
        }


        var options = scope.ServiceProvider.GetRequiredService<IOptions<GalleryOptions>>().Value;
        var remoteQualityValue = (await db.AppSettings.FindAsync("ResizerServiceQuality"))?.Value;
        var remoteWorkersValue = (await db.AppSettings.FindAsync("ResizerServiceWorkers"))?.Value;
        scope.ServiceProvider.GetService<RemoteResizer>()?.UpdateSettings(
            int.TryParse(remoteQualityValue,out var remoteQuality) ? remoteQuality : options.ResizerServiceQuality,
            int.TryParse(remoteWorkersValue,out var remoteWorkers) ? remoteWorkers : options.ResizerServiceWorkers);
        var remoteMode = (await db.AppSettings.FindAsync("ResizerServiceDecodeMode"))?.Value ?? options.ResizerServiceDecodeMode;
        var remoteBackgroundValue = (await db.AppSettings.FindAsync("ResizerServiceBackgroundWorkers"))?.Value;
        // On upgrade preserve the existing background opt-in once as the remote default.
        if (remoteBackgroundValue is null) {
            remoteBackgroundValue = (await db.AppSettings.FindAsync("BackgroundThumbnailWorkers"))?.Value
                ?? options.ResizerServiceBackgroundWorkers.ToString();
            db.AppSettings.Add(new AppSetting { Key = "ResizerServiceBackgroundWorkers", Value = remoteBackgroundValue });
            await db.SaveChangesAsync();
        }
        scope.ServiceProvider.GetService<RemoteResizer>()?.UpdateDecodeSettings(remoteMode,
            int.TryParse(remoteWorkersValue,out remoteWorkers) ? remoteWorkers : options.ResizerServiceWorkers,
            int.TryParse(remoteBackgroundValue,out var remoteBackground) ? remoteBackground : options.ResizerServiceBackgroundWorkers);
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
        var backgroundValue = (await db.AppSettings.FindAsync("BackgroundThumbnailWorkers"))?.Value;
        queueSettings.SetBackgroundWorkers(int.TryParse(backgroundValue, out var backgroundWorkers) ? backgroundWorkers : 0);
        queueSettings.SetReducedJpeg((await db.AppSettings.FindAsync("ThumbnailDecodeMode"))?.Value == "jpeg-idct");

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
        var createdAdmin = admin is null;
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = userName,
                DisplayName = configuration["BootstrapAdmin:DisplayName"] ?? "Administrator",
                RootFolder = rootPath
                , RequirePasswordChange = true
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
        if (createdAdmin && !await db.UserRoots.AnyAsync(root => root.OwnerUserId == admin.Id))
        {
            db.UserRoots.Add(new UserRoot
            {
                OwnerUserId = admin.Id,
                Name = new DirectoryInfo(rootPath).Name,
                PhysicalPath = rootPath,
                SortOrder = 0
            });
            admin.RootFolder = rootPath;
            await db.SaveChangesAsync();
        }
        if (!await userManager.IsInRoleAsync(admin, AdminRole))
            await userManager.AddToRoleAsync(admin, AdminRole);
    }

    private static async Task EnsurePasswordColumnAsync(GalleryDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AspNetUsers') WHERE name='RequirePasswordChange'";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0) {
                command.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN RequirePasswordChange INTEGER NOT NULL DEFAULT 0";
                await command.ExecuteNonQueryAsync();
            }
        } finally { await connection.CloseAsync(); }
    }

    private static async Task EnsureShareTargetTypeAsync(GalleryDbContext db)
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
            if (!columns.Contains("TargetType"))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE ShareLinks ADD COLUMN TargetType TEXT NOT NULL DEFAULT 'folder'";
                await command.ExecuteNonQueryAsync();
            }
            if (!columns.Contains("SelectedPathsJson")) {
                await using var addSelection = connection.CreateCommand();
                addSelection.CommandText = "ALTER TABLE ShareLinks ADD COLUMN SelectedPathsJson TEXT NOT NULL DEFAULT '[]'";
                await addSelection.ExecuteNonQueryAsync();
            }
            await using var normalize = connection.CreateCommand();
            normalize.CommandText = "UPDATE ShareLinks SET TargetType = 'collection' WHERE CollectionId IS NOT NULL AND TargetType <> 'collection'";
            await normalize.ExecuteNonQueryAsync();
        }
        finally { await connection.CloseAsync(); }
    }

    private static async Task EnsureUserRootSchemaAsync(GalleryDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS UserRoots (
                    Id INTEGER NOT NULL CONSTRAINT PK_UserRoots PRIMARY KEY AUTOINCREMENT,
                    OwnerUserId TEXT NOT NULL,
                    Name TEXT COLLATE NOCASE NOT NULL,
                    PhysicalPath TEXT COLLATE NOCASE NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL,
                    CONSTRAINT FK_UserRoots_AspNetUsers_OwnerUserId FOREIGN KEY (OwnerUserId) REFERENCES AspNetUsers (Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_UserRoots_OwnerUserId_PhysicalPath ON UserRoots (OwnerUserId, PhysicalPath);
                """;
            await command.ExecuteNonQueryAsync();

            // Legacy RootFolder is migration input, never an ongoing source of assignments.
            // Keep E:\ and / intact; trimming separators turns volume roots into other paths.
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await using var seed = connection.CreateCommand();
                seed.Transaction = transaction;
                seed.CommandText = """
                    INSERT OR IGNORE INTO UserRoots (OwnerUserId, Name, PhysicalPath, SortOrder, CreatedAtUtc)
                    SELECT Id, RootFolder, RootFolder, 0, strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now')
                    FROM AspNetUsers AS u
                    WHERE trim(RootFolder) <> ''
                      AND NOT EXISTS (SELECT 1 FROM UserRoots WHERE OwnerUserId = u.Id)
                      AND NOT EXISTS (SELECT 1 FROM AppSettings WHERE Key = 'Migration.UserRoots.SeededV2');
                    INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('Migration.UserRoots.SeededV2', 'true');
                    """;
                await seed.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }

            var roots = new List<(long Id, string Path)>();
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT Id, PhysicalPath FROM UserRoots WHERE trim(Name) = '' OR Name = PhysicalPath";
                await using var reader = await read.ExecuteReaderAsync();
                while (await reader.ReadAsync()) roots.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            foreach (var root in roots)
            {
                var name = new DirectoryInfo(root.Path).Name;
                if (string.IsNullOrWhiteSpace(name)) name = root.Path;
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE UserRoots SET Name = $name WHERE Id = $id";
                var nameParameter = update.CreateParameter(); nameParameter.ParameterName = "$name"; nameParameter.Value = name; update.Parameters.Add(nameParameter);
                var idParameter = update.CreateParameter(); idParameter.ParameterName = "$id"; idParameter.Value = root.Id; update.Parameters.Add(idParameter);
                await update.ExecuteNonQueryAsync();
            }

            await using var migrate = connection.CreateCommand();
            migrate.CommandText = """
                UPDATE ShareLinks
                SET RelativePath = '@root-' || (SELECT Id FROM UserRoots WHERE OwnerUserId = ShareLinks.OwnerUserId ORDER BY SortOrder, Id LIMIT 1)
                    || CASE WHEN RelativePath = '' THEN '' ELSE '/' || replace(RelativePath, '\\', '/') END
                WHERE CollectionId IS NULL AND RelativePath NOT LIKE '@root-%'
                  AND EXISTS (SELECT 1 FROM UserRoots WHERE OwnerUserId = ShareLinks.OwnerUserId);
                UPDATE CollectionFolders
                SET RelativePath = '@root-' || (
                    SELECT UserRoots.Id FROM UserRoots
                    JOIN Collections ON Collections.OwnerUserId = UserRoots.OwnerUserId
                    WHERE Collections.Id = CollectionFolders.CollectionId ORDER BY UserRoots.SortOrder, UserRoots.Id LIMIT 1)
                    || CASE WHEN RelativePath = '' THEN '' ELSE '/' || replace(RelativePath, '\\', '/') END
                WHERE RelativePath NOT LIKE '@root-%'
                  AND EXISTS (SELECT 1 FROM Collections JOIN UserRoots ON UserRoots.OwnerUserId = Collections.OwnerUserId WHERE Collections.Id = CollectionFolders.CollectionId);
                UPDATE FolderRules
                SET RelativePath = '@root-' || (SELECT Id FROM UserRoots WHERE OwnerUserId = FolderRules.OwnerUserId ORDER BY SortOrder, Id LIMIT 1)
                    || CASE WHEN RelativePath = '' THEN '' ELSE '/' || replace(RelativePath, '\\', '/') END
                WHERE RelativePath NOT LIKE '@root-%'
                  AND EXISTS (SELECT 1 FROM UserRoots WHERE OwnerUserId = FolderRules.OwnerUserId);
                """;
            await migrate.ExecuteNonQueryAsync();
        }
        finally { await connection.CloseAsync(); }
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
