using System.Globalization;
using System.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;
using WebGallery.ViewModels;

namespace WebGallery.Controllers;

[Authorize(Roles = DatabaseInitializer.AdminRole)]
public sealed class AdminController(
    UserManager<ApplicationUser> userManager,
    GalleryDbContext db,
    IOptions<GalleryOptions> galleryOptions,
    ThumbnailQueueSettings thumbnailQueueSettings,
    LoginSecuritySettings loginSecuritySettings,
    ILogger<AdminController> logger) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = await userManager.Users.Include(x => x.Roots).OrderBy(x => x.UserName).ToListAsync();
        var rows = new List<AdminUserViewModel>();
        foreach (var user in users)
        {
            rows.Add(new AdminUserViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? "",
                DisplayName = user.DisplayName,
                Roots = user.Roots.OrderBy(root => root.SortOrder).ThenBy(root => root.Id)
                    .Select(root => new AdminUserRootViewModel { Id = root.Id, Name = root.Name, PhysicalPath = root.PhysicalPath }).ToList(),
                IsAdmin = await userManager.IsInRoleAsync(user, DatabaseInitializer.AdminRole)
            });
        }
        var title = (await db.AppSettings.FindAsync("AppTitle"))?.Value ?? galleryOptions.Value.AppTitle;
        var theme = NormalizeTheme((await db.AppSettings.FindAsync("Theme"))?.Value) ?? "retro";
        var concurrencyValue = (await db.AppSettings.FindAsync("ThumbnailConcurrency"))?.Value;
        var concurrency = int.TryParse(concurrencyValue, out var parsedConcurrency)
            ? ThumbnailQueueSettings.Clamp(parsedConcurrency)
            : thumbnailQueueSettings.MaxConcurrency;
        var login = loginSecuritySettings.Current;
        return View(new AdminIndexViewModel
        {
            Users = rows,
            AppTitle = title,
            Theme = theme,
            ThumbnailConcurrency = concurrency,
            LoginDelayAfterFailures = login.DelayAfterFailures,
            LoginDelayIncrementSeconds = login.DelayIncrementSeconds,
            LoginUserFailureLimit = login.UserFailureLimit,
            LoginUserCooldownMinutes = (int)login.UserCooldown.TotalMinutes,
            LoginIpFailureLimit = login.IpFailureLimit,
            LoginIpCooldownMinutes = (int)login.IpCooldown.TotalMinutes
        });
    }

    [HttpGet]
    public async Task<IActionResult> Logs(
        string? eventType,
        string? ownerId,
        string? shareType,
        string? clientIp,
        string? search,
        string? from,
        string? to,
        int page = 1)
    {
        const int pageSize = 100;
        var normalizedEventType = NormalizeEventType(eventType);
        var normalizedShareType = NormalizeShareType(shareType);
        var normalizedOwnerId = ownerId?.Trim() ?? "";
        var normalizedClientIp = clientIp?.Trim() ?? "";
        var normalizedSearch = search?.Trim() ?? "";
        var fromUnix = ParseLocalDateBoundary(from, false, out var normalizedFrom);
        var toUnix = ParseLocalDateBoundary(to, true, out var normalizedTo);

        IQueryable<ShareAuditEvent> query = db.ShareAuditEvents.AsNoTracking();
        if (!string.IsNullOrEmpty(normalizedEventType))
            query = query.Where(item => item.EventType == normalizedEventType);
        if (!string.IsNullOrEmpty(normalizedOwnerId))
            query = query.Where(item => item.ShareLink!.OwnerUserId == normalizedOwnerId);
        if (normalizedShareType == "collection")
            query = query.Where(item => item.ShareLink!.CollectionId != null);
        else if (normalizedShareType == "folder")
            query = query.Where(item => item.ShareLink!.TargetType == ShareTargetTypes.Folder);
        else if (normalizedShareType == "file")
            query = query.Where(item => item.ShareLink!.TargetType == ShareTargetTypes.File);
        if (!string.IsNullOrEmpty(normalizedClientIp))
        {
            var pattern = $"%{EscapeLikePattern(normalizedClientIp)}%";
            query = query.Where(item => EF.Functions.Like(item.ClientIp, pattern, "\\"));
        }
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";
            query = query.Where(item =>
                EF.Functions.Like(item.TargetPath, pattern, "\\")
                || EF.Functions.Like(item.Details, pattern, "\\")
                || EF.Functions.Like(item.VisitorHash, pattern, "\\")
                || EF.Functions.Like(item.ShareLink!.Owner!.UserName!, pattern, "\\")
                || (item.ShareLink.Collection != null && EF.Functions.Like(item.ShareLink.Collection.Name, pattern, "\\")));
        }
        if (fromUnix.HasValue)
            query = query.Where(item => item.OccurredAtUnixSeconds >= fromUnix.Value);
        if (toUnix.HasValue)
            query = query.Where(item => item.OccurredAtUnixSeconds < toUnix.Value);

        var eventCounts = await query
            .GroupBy(item => item.EventType)
            .Select(group => new { EventType = group.Key, Count = group.Count() })
            .ToListAsync();
        var total = eventCounts.Sum(item => item.Count);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(Math.Max(1, page), totalPages);
        var events = await query
            .Include(item => item.ShareLink)!.ThenInclude(link => link!.Owner)
            .Include(item => item.ShareLink)!.ThenInclude(link => link!.Collection)
            .OrderByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var owners = await userManager.Users.AsNoTracking()
            .OrderBy(user => user.UserName)
            .Select(user => new AdminLogOwnerOptionViewModel { Id = user.Id, UserName = user.UserName ?? "" })
            .ToListAsync();

        return View(new AdminLogsViewModel
        {
            Events = events.Select(item => new AdminLogRowViewModel
            {
                Id = item.Id,
                OccurredAtUtc = item.OccurredAtUtc,
                EventType = item.EventType,
                OwnerUserName = item.ShareLink?.Owner?.UserName ?? "Unknown user",
                ShareType = item.ShareLink?.TargetType == ShareTargetTypes.File ? "File" : item.ShareLink?.Collection is null ? "Folder" : "Collection",
                ShareLabel = item.ShareLink?.Collection?.Name
                    ?? (item.ShareLink?.TargetType == ShareTargetTypes.File
                        ? Path.GetFileName(item.ShareLink.RelativePath)
                        : string.IsNullOrWhiteSpace(item.ShareLink?.RelativePath) ? "Home" : item.ShareLink.RelativePath.Replace('\\', '/')),
                TargetPath = item.TargetPath,
                Details = item.Details,
                ItemCount = item.ItemCount,
                ClientIp = item.ClientIp,
                VisitorHash = item.VisitorHash
            }).ToList(),
            Owners = owners,
            EventType = normalizedEventType,
            OwnerId = normalizedOwnerId,
            ShareType = normalizedShareType,
            ClientIp = normalizedClientIp,
            Search = normalizedSearch,
            FromDate = normalizedFrom,
            ToDate = normalizedTo,
            TotalEvents = total,
            AccessCount = eventCounts.Where(item => item.EventType == ShareAuditEventTypes.Access).Sum(item => item.Count),
            ViewCount = eventCounts.Where(item => item.EventType == ShareAuditEventTypes.View).Sum(item => item.Count),
            DownloadCount = eventCounts.Where(item => ShareAuditEventTypes.IsDownload(item.EventType)).Sum(item => item.Count),
            Page = page,
            TotalPages = totalPages
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string userName, string displayName, string? rootFolder, string password, bool isAdmin = false)
    {
        var hasRoot = !string.IsNullOrWhiteSpace(rootFolder);
        if (hasRoot && !TryValidateRoot(rootFolder!, out rootFolder, out var rootError))
        {
            TempData["Error"] = rootError;
            return RedirectToAction(nameof(Index));
        }
        var user = new ApplicationUser { UserName = userName.Trim(), DisplayName = displayName.Trim(), RootFolder = hasRoot ? rootFolder! : "" };
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded && hasRoot)
        {
            db.UserRoots.Add(new UserRoot { OwnerUserId = user.Id, Name = GetDefaultRootName(rootFolder!), PhysicalPath = rootFolder!, SortOrder = 0 });
            await db.SaveChangesAsync();
        }
        if (result.Succeeded && isAdmin) result = await userManager.AddToRoleAsync(user, DatabaseInitializer.AdminRole);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "User created." : string.Join("; ", result.Errors.Select(x => x.Description));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(string id, string displayName, bool isAdmin, string? newPassword)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        user.DisplayName = displayName.Trim();
        var result = await userManager.UpdateAsync(user);
        if (result.Succeeded)
        {
            var currentlyAdmin = await userManager.IsInRoleAsync(user, DatabaseInitializer.AdminRole);
            if (isAdmin && !currentlyAdmin) result = await userManager.AddToRoleAsync(user, DatabaseInitializer.AdminRole);
            else if (!isAdmin && currentlyAdmin && user.Id != userManager.GetUserId(User))
                result = await userManager.RemoveFromRoleAsync(user, DatabaseInitializer.AdminRole);
        }
        if (result.Succeeded && !string.IsNullOrWhiteSpace(newPassword))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            result = await userManager.ResetPasswordAsync(user, token, newPassword);
        }
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "User saved." : string.Join("; ", result.Errors.Select(x => x.Description));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUserRoot(string id, string physicalPath, string? name)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
            return UserRootFailure(id, "The folder was not added because the user no longer exists.");
        if (!TryValidateRoot(physicalPath, out physicalPath, out var error))
            return UserRootFailure(id, error);

        try
        {
            if (await db.UserRoots.AnyAsync(root => root.OwnerUserId == id && root.PhysicalPath == physicalPath))
                return UserRootFailure(id, $"The folder is already assigned to {user.UserName}: {physicalPath}");

            var sortOrder = await db.UserRoots.Where(root => root.OwnerUserId == id)
                .Select(root => (int?)root.SortOrder).MaxAsync() ?? -1;
            db.UserRoots.Add(new UserRoot
            {
                OwnerUserId = id,
                Name = NormalizeRootName(name, physicalPath),
                PhysicalPath = physicalPath,
                SortOrder = sortOrder + 1
            });
            if (string.IsNullOrWhiteSpace(user.RootFolder)) user.RootFolder = physicalPath;
            await db.SaveChangesAsync();
            TempData["Success"] = $"Folder added to {user.UserName}: {physicalPath}";
            return RedirectToUser(id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not add gallery root {PhysicalPath} to user {UserId}", physicalPath, id);
            return UserRootFailure(id, DescribeRootSaveFailure(exception, physicalPath));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUserRoot(int rootId, string physicalPath, string? name)
    {
        var root = await db.UserRoots.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == rootId);
        if (root is null) return NotFound();
        if (!TryValidateRoot(physicalPath, out physicalPath, out var error))
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }
        if (await db.UserRoots.AnyAsync(item => item.OwnerUserId == root.OwnerUserId && item.Id != rootId && item.PhysicalPath == physicalPath))
        {
            TempData["Error"] = "This folder is already assigned to the user.";
            return RedirectToAction(nameof(Index));
        }
        var oldPath = root.PhysicalPath;
        root.PhysicalPath = physicalPath;
        root.Name = NormalizeRootName(name, physicalPath);
        if (root.Owner is not null && string.Equals(root.Owner.RootFolder, oldPath, StringComparison.OrdinalIgnoreCase))
            root.Owner.RootFolder = physicalPath;
        await db.SaveChangesAsync();
        TempData["Success"] = "Folder saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUserRoot(int rootId)
    {
        var root = await db.UserRoots.Include(item => item.Owner).SingleOrDefaultAsync(item => item.Id == rootId);
        if (root is null) return NotFound();
        var owner = root.Owner;
        db.UserRoots.Remove(root);
        await db.SaveChangesAsync();
        if (owner is not null)
        {
            owner.RootFolder = await db.UserRoots.Where(item => item.OwnerUserId == owner.Id)
                .OrderBy(item => item.SortOrder).ThenBy(item => item.Id).Select(item => item.PhysicalPath).FirstOrDefaultAsync() ?? "";
            await db.SaveChangesAsync();
        }
        TempData["Success"] = "Folder removed from the user. Files were not deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (id == userManager.GetUserId(User))
        {
            TempData["Error"] = "You cannot delete the account currently in use.";
            return RedirectToAction(nameof(Index));
        }
        var user = await userManager.FindByIdAsync(id);
        if (user is not null) await userManager.DeleteAsync(user);
        TempData["Success"] = "User deleted. Files in the root folder were not deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        string appTitle,
        string theme,
        int thumbnailConcurrency,
        int loginDelayAfterFailures,
        int loginDelayIncrementSeconds,
        int loginUserFailureLimit,
        int loginUserCooldownMinutes,
        int loginIpFailureLimit,
        int loginIpCooldownMinutes)
    {
        if (thumbnailConcurrency is < ThumbnailQueueSettings.MinimumConcurrency or > ThumbnailQueueSettings.MaximumConcurrency)
        {
            TempData["Error"] = $"Thumbnail concurrency must be between {ThumbnailQueueSettings.MinimumConcurrency} and {ThumbnailQueueSettings.MaximumConcurrency}.";
            return RedirectToAction(nameof(Index));
        }
        var normalizedTheme = NormalizeTheme(theme);
        if (normalizedTheme is null)
        {
            TempData["Error"] = "Theme must be Retro or Modern.";
            return RedirectToAction(nameof(Index));
        }
        if (!LoginSecuritySettings.TryCreate(
            loginDelayAfterFailures,
            loginDelayIncrementSeconds,
            loginUserFailureLimit,
            loginUserCooldownMinutes,
            loginIpFailureLimit,
            loginIpCooldownMinutes,
            out var loginOptions,
            out var loginError))
        {
            TempData["Error"] = loginError;
            return RedirectToAction(nameof(Index));
        }
        var setting = await db.AppSettings.FindAsync("AppTitle");
        if (setting is null) db.AppSettings.Add(new AppSetting { Key = "AppTitle", Value = appTitle.Trim() });
        else setting.Value = appTitle.Trim();
        var concurrencySetting = await db.AppSettings.FindAsync("ThumbnailConcurrency");
        if (concurrencySetting is null)
            db.AppSettings.Add(new AppSetting { Key = "ThumbnailConcurrency", Value = thumbnailConcurrency.ToString() });
        else
            concurrencySetting.Value = thumbnailConcurrency.ToString();
        var themeSetting = await db.AppSettings.FindAsync("Theme");
        if (themeSetting is null)
            db.AppSettings.Add(new AppSetting { Key = "Theme", Value = normalizedTheme });
        else
            themeSetting.Value = normalizedTheme;
        await UpsertSettingAsync("LoginDelayAfterFailures", loginDelayAfterFailures);
        await UpsertSettingAsync("LoginDelayIncrementSeconds", loginDelayIncrementSeconds);
        await UpsertSettingAsync("LoginUserFailureLimit", loginUserFailureLimit);
        await UpsertSettingAsync("LoginUserCooldownMinutes", loginUserCooldownMinutes);
        await UpsertSettingAsync("LoginIpFailureLimit", loginIpFailureLimit);
        await UpsertSettingAsync("LoginIpCooldownMinutes", loginIpCooldownMinutes);
        await db.SaveChangesAsync();
        thumbnailQueueSettings.Update(thumbnailConcurrency);
        loginSecuritySettings.Update(loginOptions);
        TempData["Success"] = "System settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task UpsertSettingAsync(string key, int value)
    {
        var setting = await db.AppSettings.FindAsync(key);
        if (setting is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value.ToString(CultureInfo.InvariantCulture) });
        else setting.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? NormalizeTheme(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "retro" => "retro",
        "modern" => "modern",
        _ => null
    };

    private static string NormalizeEventType(string? value)
    {
        var normalized = value?.Trim() ?? "";
        return normalized is ShareAuditEventTypes.Access
            or ShareAuditEventTypes.View
            or ShareAuditEventTypes.DownloadFile
            or ShareAuditEventTypes.DownloadFolder
            or ShareAuditEventTypes.DownloadCollection
            or ShareAuditEventTypes.DownloadSelection
            ? normalized
            : "";
    }

    private static string NormalizeShareType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "folder" => "folder",
        "collection" => "collection",
        "file" => "file",
        _ => ""
    };

    private static long? ParseLocalDateBoundary(string? value, bool endExclusive, out string normalized)
    {
        normalized = "";
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var localDateTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddDays(endExclusive ? 1 : 0);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUnixTimeSeconds();
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Root folder is required.");
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    private static string GetDefaultRootName(string physicalPath)
    {
        var name = new DirectoryInfo(physicalPath).Name;
        return string.IsNullOrWhiteSpace(name) ? physicalPath : name;
    }

    private static string NormalizeRootName(string? name, string physicalPath)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? GetDefaultRootName(physicalPath) : name.Trim();
        if (normalized.Length > 160) normalized = normalized[..160];
        return normalized;
    }

    private static bool TryValidateRoot(string path, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        try
        {
            normalized = NormalizeRoot(path);
            var attributes = System.IO.File.GetAttributes(normalized);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                error = $"The path is a file, not a folder: {normalized}";
                return false;
            }

            // Force one enumeration while still in the Admin request so permission
            // problems become a useful validation message instead of a Gallery 500.
            using var probe = Directory.EnumerateFileSystemEntries(normalized).GetEnumerator();
            _ = probe.MoveNext();
            return true;
        }
        catch (FileNotFoundException)
        {
            error = $"The folder does not exist: {(string.IsNullOrWhiteSpace(normalized) ? path : normalized)}";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            error = $"The folder does not exist: {(string.IsNullOrWhiteSpace(normalized) ? path : normalized)}";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = $"Access was denied for {path}. Grant the IIS application pool identity Read & Execute permission on this folder and its contents.";
            return false;
        }
        catch (SecurityException)
        {
            error = $"Windows security blocked access to {path}. Grant the IIS application pool identity Read & Execute permission.";
            return false;
        }
        catch (IOException exception)
        {
            error = $"Windows could not open {path}: {exception.Message}";
            return false;
        }
        catch (ArgumentException)
        {
            error = "The root folder path is invalid.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "The root folder path format is not supported.";
            return false;
        }
    }

    private IActionResult UserRootFailure(string userId, string message)
    {
        TempData["Error"] = message;
        return RedirectToUser(userId, showRootError: true);
    }

    private static string UserAnchor(string userId) => $"user-{userId}";

    private IActionResult RedirectToUser(string userId, bool showRootError = false)
    {
        var url = showRootError
            ? Url.Action(nameof(Index), new { rootErrorUserId = userId })
            : Url.Action(nameof(Index));
        return Redirect($"{url}#{Uri.EscapeDataString(UserAnchor(userId))}");
    }

    private static string DescribeRootSaveFailure(Exception exception, string physicalPath)
    {
        var cause = exception.GetBaseException();
        if (cause is SqliteException sqlite)
        {
            return sqlite.SqliteErrorCode switch
            {
                5 or 6 => "The folder was validated, but the database is busy or locked. Wait a moment and try again.",
                8 => "The folder was validated, but the Gallery database is read-only. Check Modify permission on the data folder.",
                13 => "The folder was validated, but the disk containing the Gallery database is full.",
                19 => $"The folder could not be saved because it conflicts with an existing folder assignment: {physicalPath}",
                _ => $"SQLite could not save the folder (error {sqlite.SqliteErrorCode}): {sqlite.Message}"
            };
        }

        return $"The folder could not be added because {cause.Message}";
    }
}
