using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    ThumbnailQueueSettings thumbnailQueueSettings) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = await userManager.Users.OrderBy(x => x.UserName).ToListAsync();
        var rows = new List<AdminUserViewModel>();
        foreach (var user in users)
        {
            rows.Add(new AdminUserViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? "",
                DisplayName = user.DisplayName,
                RootFolder = user.RootFolder,
                IsAdmin = await userManager.IsInRoleAsync(user, DatabaseInitializer.AdminRole)
            });
        }
        var title = (await db.AppSettings.FindAsync("AppTitle"))?.Value ?? galleryOptions.Value.AppTitle;
        var theme = NormalizeTheme((await db.AppSettings.FindAsync("Theme"))?.Value) ?? "retro";
        var concurrencyValue = (await db.AppSettings.FindAsync("ThumbnailConcurrency"))?.Value;
        var concurrency = int.TryParse(concurrencyValue, out var parsedConcurrency)
            ? ThumbnailQueueSettings.Clamp(parsedConcurrency)
            : thumbnailQueueSettings.MaxConcurrency;
        return View(new AdminIndexViewModel { Users = rows, AppTitle = title, Theme = theme, ThumbnailConcurrency = concurrency });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string userName, string displayName, string rootFolder, string password, bool isAdmin = false)
    {
        if (!TryValidateRoot(rootFolder, out rootFolder, out var rootError))
        {
            TempData["Error"] = rootError;
            return RedirectToAction(nameof(Index));
        }
        var user = new ApplicationUser { UserName = userName.Trim(), DisplayName = displayName.Trim(), RootFolder = rootFolder };
        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded && isAdmin) result = await userManager.AddToRoleAsync(user, DatabaseInitializer.AdminRole);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "User created." : string.Join("; ", result.Errors.Select(x => x.Description));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(string id, string displayName, string rootFolder, bool isAdmin, string? newPassword)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (!TryValidateRoot(rootFolder, out rootFolder, out var rootError))
        {
            TempData["Error"] = rootError;
            return RedirectToAction(nameof(Index));
        }
        user.DisplayName = displayName.Trim();
        user.RootFolder = rootFolder;
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
    public async Task<IActionResult> SaveSettings(string appTitle, string theme, int thumbnailConcurrency)
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
        await db.SaveChangesAsync();
        thumbnailQueueSettings.Update(thumbnailConcurrency);
        TempData["Success"] = "System settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private static string? NormalizeTheme(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "retro" => "retro",
        "modern" => "modern",
        _ => null
    };

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Root folder is required.");
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    private static bool TryValidateRoot(string path, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        try
        {
            normalized = NormalizeRoot(path);
            if (!Directory.Exists(normalized))
            {
                error = $"Root folder not found: {normalized}";
                return false;
            }

            // Force one enumeration while still in the Admin request so permission
            // problems become a useful validation message instead of a Gallery 500.
            using var probe = Directory.EnumerateFileSystemEntries(normalized).GetEnumerator();
            _ = probe.MoveNext();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = $"IIS does not have permission to read the root folder: {path}";
            return false;
        }
        catch (IOException)
        {
            error = $"The root folder could not be opened: {path}";
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
}
