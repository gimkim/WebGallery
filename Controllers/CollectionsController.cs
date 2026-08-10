using System.Security.Cryptography;
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

[Authorize]
public sealed class CollectionsController(
    UserManager<ApplicationUser> userManager,
    GalleryDbContext db,
    FileSystemService files,
    IOptions<GalleryOptions> options) : Controller
{
    public async Task<IActionResult> Index()
    {
        var owner = await userManager.GetUserAsync(User);
        if (owner is null) return Challenge();
        var collections = await db.Collections
            .Where(x => x.OwnerUserId == owner.Id)
            .Include(x => x.Folders)
            .Include(x => x.ShareLinks.Where(link => !link.IsRevoked))
            .OrderBy(x => x.Name)
            .ToListAsync();
        var collectionModels = collections.Select(collection => new CollectionManagementViewModel
        {
            Id = collection.Id,
            Name = collection.Name,
            Folders = collection.Folders
                .OrderBy(folder => folder.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .Select(folder => new CollectionFolderCardViewModel
                {
                    MembershipId = folder.Id,
                    RelativePath = folder.RelativePath,
                    Item = GetCollectionFolderItem(owner, folder.RelativePath)
                })
                .ToList(),
            ShareLinks = collection.ShareLinks
                .Where(link => !link.IsRevoked)
                .OrderByDescending(link => link.CreatedAtUtc)
                .ToList()
        }).ToList();
        return View(new CollectionsIndexViewModel
        {
            OwnerUserName = owner.UserName ?? "",
            Collections = collectionModels,
            DefaultItemsPerRow = options.Value.DefaultItemsPerRow
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? name)
    {
        var owner = await userManager.GetUserAsync(User);
        if (owner is null) return Challenge();
        var normalizedName = NormalizeName(name);
        if (normalizedName is null)
        {
            TempData["Error"] = "Collection name must be between 1 and 80 characters.";
            return RedirectToAction(nameof(Index));
        }
        if (await db.Collections.AnyAsync(x => x.OwnerUserId == owner.Id && x.Name == normalizedName))
        {
            TempData["Error"] = "A collection with this name already exists.";
            return RedirectToAction(nameof(Index));
        }
        db.Collections.Add(new GalleryCollection { OwnerUserId = owner.Id, Name = normalizedName });
        await db.SaveChangesAsync();
        TempData["Success"] = "Collection created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFolders(int collectionId, string[] folderPaths, string? returnPath)
    {
        var owner = await userManager.GetUserAsync(User);
        if (owner is null) return Challenge();
        var collection = await db.Collections.Include(x => x.Folders)
            .SingleOrDefaultAsync(x => x.Id == collectionId && x.OwnerUserId == owner.Id);
        if (collection is null) return NotFound();

        var candidates = new List<string>();
        foreach (var path in folderPaths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = files.NormalizeRelativePath(path).Replace(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(normalized)) continue;
            var resolved = files.ResolvePath(owner, normalized);
            if (!Directory.Exists(resolved) || FileSystemService.IsIgnoredFileSystemEntry(resolved)) continue;
            candidates.Add(normalized);
        }

        var added = 0;
        foreach (var candidate in candidates.OrderBy(x => x.Count(c => c == '/')))
        {
            if (collection.Folders.Any(x => FileSystemService.IsWithinShareScope(x.RelativePath, candidate))) continue;
            var redundantChildren = collection.Folders
                .Where(x => FileSystemService.IsWithinShareScope(candidate, x.RelativePath))
                .ToList();
            db.CollectionFolders.RemoveRange(redundantChildren);
            collection.Folders.Add(new GalleryCollectionFolder { RelativePath = candidate });
            added++;
        }
        await db.SaveChangesAsync();
        TempData[added > 0 ? "Success" : "Error"] = added > 0
            ? $"Added {added} folder{(added == 1 ? "" : "s")} to {collection.Name}."
            : "No new folders were added.";
        return RedirectToAction("Index", "Gallery", new { path = files.NormalizeRelativePath(returnPath) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFolder(int id)
    {
        var owner = await userManager.GetUserAsync(User);
        var folder = owner is null ? null : await db.CollectionFolders
            .Include(x => x.Collection)
            .SingleOrDefaultAsync(x => x.Id == id && x.Collection!.OwnerUserId == owner.Id);
        if (folder is null) return NotFound();
        db.CollectionFolders.Remove(folder);
        await db.SaveChangesAsync();
        TempData["Success"] = "Folder removed from collection.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var owner = await userManager.GetUserAsync(User);
        var collection = owner is null ? null : await db.Collections
            .Include(x => x.Folders).Include(x => x.ShareLinks)
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner.Id);
        if (collection is null) return NotFound();
        db.ShareLinks.RemoveRange(collection.ShareLinks);
        db.CollectionFolders.RemoveRange(collection.Folders);
        db.Collections.Remove(collection);
        await db.SaveChangesAsync();
        TempData["Success"] = "Collection deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShare(int collectionId, int? itemsPerRow, string? viewMode)
    {
        var owner = await userManager.GetUserAsync(User);
        var collection = owner is null ? null : await db.Collections
            .Include(x => x.Folders)
            .SingleOrDefaultAsync(x => x.Id == collectionId && x.OwnerUserId == owner.Id);
        if (collection is null) return NotFound();
        if (collection.Folders.Count == 0)
        {
            TempData["Error"] = "Add at least one folder before creating a share link.";
            return RedirectToAction(nameof(Index));
        }
        var link = new ShareLink
        {
            OwnerUserId = owner!.Id,
            CollectionId = collection.Id,
            RelativePath = "",
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
            Sort = "name",
            Direction = "asc",
            ItemsPerRow = itemsPerRow is >= 2 and <= 10 ? itemsPerRow.Value : options.Value.DefaultItemsPerRow,
            ViewMode = string.Equals(viewMode, "list", StringComparison.OrdinalIgnoreCase) ? "list" : "grid"
        };
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync();
        TempData["Success"] = "Collection share link created.";
        TempData["CreatedCollectionShareLinkId"] = link.Id;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeShare(int id)
    {
        var owner = await userManager.GetUserAsync(User);
        var link = owner is null ? null : await db.ShareLinks
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner.Id && x.CollectionId != null);
        if (link is null) return NotFound();
        link.IsRevoked = true;
        await db.SaveChangesAsync();
        TempData["Success"] = "Collection share link revoked.";
        return RedirectToAction(nameof(Index));
    }

    private static string? NormalizeName(string? name)
    {
        var value = name?.Trim();
        return value is { Length: >= 1 and <= 80 } ? value : null;
    }

    private GalleryItemViewModel? GetCollectionFolderItem(ApplicationUser owner, string relativePath)
    {
        try { return files.GetDirectoryItem(owner, relativePath); }
        catch (InvalidOperationException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
