using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;
using WebGallery.ViewModels;

namespace WebGallery.Controllers;

public sealed class GalleryController(
    UserManager<ApplicationUser> userManager,
    GalleryDbContext db,
    FileSystemService files,
    ThumbnailService thumbnails,
    InvalidShareTokenLimiter invalidShareTokenLimiter,
    IOptions<GalleryOptions> options) : Controller
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [Authorize]
    public async Task<IActionResult> Index(string? path, string? sort = null, string? dir = null, string? focus = null)
    {
        var owner = await userManager.GetUserAsync(User);
        if (owner is null) return Challenge();
        var order = ResolveSortOrder(sort, dir);
        return await RenderAsync(owner, path, order.Sort, order.Direction, "private", null, "", canManage: true, focus, null, null);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Share(string? token, string? path, string? sort = null, string? dir = null, string? focus = null, int? itemsPerRow = null, string? view = null)
    {
        var clientAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cooldown = invalidShareTokenLimiter.GetCooldown(clientAddress);
        if (cooldown.IsActive) return ShareCooldown(cooldown);
        if (!IsShareTokenFormatValid(token)) return InvalidShareToken(clientAddress);

        var link = await db.ShareLinks
            .Include(x => x.Owner)
            .Include(x => x.Collection).ThenInclude(x => x!.Folders)
            .SingleOrDefaultAsync(x => x.Token == token && !x.IsRevoked);
        if (link?.Owner is null) return InvalidShareToken(clientAddress);
        var order = ResolveSortOrder(sort, dir);
        if (link.Collection is not null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return await RenderCollectionRootAsync(link, order.Sort, order.Direction,
                    NormalizeItemsPerRow(itemsPerRow), NormalizeViewMode(view));
            var requested = files.NormalizeRelativePath(path).Replace(Path.DirectorySeparatorChar, '/');
            var collectionRoot = FindCollectionRoot(link.Collection, requested);
            if (collectionRoot is null) return NotFound();
            return await RenderAsync(link.Owner, requested, order.Sort, order.Direction, "share", token,
                collectionRoot.RelativePath, canManage: false, focus, NormalizeItemsPerRow(itemsPerRow),
                NormalizeViewMode(view), link.Collection);
        }
        var normalized = string.IsNullOrEmpty(path) ? link.RelativePath : files.NormalizeRelativePath(path);
        if (!FileSystemService.IsWithinShareScope(link.RelativePath, normalized)) return NotFound();
        return await RenderAsync(link.Owner, normalized, order.Sort, order.Direction, "share", token, link.RelativePath,
            canManage: false, focus, NormalizeItemsPerRow(itemsPerRow), NormalizeViewMode(view));
    }

    private IActionResult InvalidShareToken(string clientAddress)
    {
        var cooldown = invalidShareTokenLimiter.RecordInvalidToken(clientAddress);
        return cooldown.IsActive ? ShareCooldown(cooldown) : NotFound();
    }

    private IActionResult ShareCooldown(ShareTokenCooldown cooldown)
    {
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        Response.Headers.RetryAfter = cooldown.RetryAfterSeconds.ToString();
        return View("ShareCooldown", new CooldownViewModel
        {
            RetryAfterSeconds = cooldown.RetryAfterSeconds,
            Title = "Too many invalid share links",
            Message = "Share link requests from this connection are temporarily paused. Try again in"
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShare(string? path, string? sort, string? dir, int? itemsPerRow, string? viewMode)
    {
        var owner = await userManager.GetUserAsync(User);
        if (owner is null) return Challenge();
        var normalized = files.NormalizeRelativePath(path);
        var resolved = files.ResolvePath(owner, normalized);
        if (!Directory.Exists(resolved) || (!string.IsNullOrEmpty(normalized) && FileSystemService.IsIgnoredFileSystemEntry(resolved))) return NotFound();
        var normalizedSort = NormalizeSort(sort) ?? "name";
        var normalizedDirection = NormalizeDirection(dir) ?? "asc";
        var shareLink = new ShareLink
        {
            OwnerUserId = owner.Id,
            RelativePath = normalized,
            Token = CreateToken(),
            Sort = normalizedSort,
            Direction = normalizedDirection,
            ItemsPerRow = NormalizeItemsPerRow(itemsPerRow) ?? options.Value.DefaultItemsPerRow,
            ViewMode = NormalizeViewMode(viewMode) ?? "grid"
        };
        db.ShareLinks.Add(shareLink);
        await db.SaveChangesAsync();
        TempData["Success"] = "Share link created.";
        TempData["OpenSharePanel"] = true;
        TempData["CreatedShareLinkId"] = shareLink.Id;
        return RedirectToAction(nameof(Index), new { path = normalized, sort = normalizedSort, dir = normalizedDirection });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeShare(int id, string? path)
    {
        var owner = await userManager.GetUserAsync(User);
        var link = owner is null ? null : await db.ShareLinks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner.Id);
        if (link is null) return NotFound();
        link.IsRevoked = true;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { path });
    }

    [AllowAnonymous]
    public async Task<IActionResult> Thumbnail(string mode, string? userName, string? token, string path, string? stamp, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
        if (access is null) return NotFound();
        var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        if (!FileSystemService.IsImage(Path.GetExtension(fullPath))) return NotFound();
        try
        {
            var priority = string.Equals(Request.Headers["X-Thumbnail-Priority"], "visible", StringComparison.OrdinalIgnoreCase)
                ? ThumbnailPriority.Visible
                : ThumbnailPriority.Normal;
            var cacheFile = await thumbnails.GetOrCreateAsync(access.Value.Owner.Id, fullPath, priority, cancellationToken);
            Response.Headers.CacheControl = string.IsNullOrWhiteSpace(stamp)
                ? "private, no-cache"
                : "private, max-age=31536000, immutable";
            return PhysicalFile(cacheFile, "image/webp", enableRangeProcessing: true);
        }
        catch (UnknownImageFormatException) { return NotFound(); }
        catch (ThumbnailQueueFullException)
        {
            Response.Headers["Retry-After"] = "1";
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    [AllowAnonymous]
    public async Task<IActionResult> ViewFile(string mode, string? userName, string? token, string path, string? stamp, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
        if (access is null) return NotFound();
        var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        if (!FileSystemService.IsImage(Path.GetExtension(fullPath))) return NotFound();
        Response.Headers.CacheControl = string.IsNullOrWhiteSpace(stamp)
            ? "private, no-cache"
            : "private, max-age=31536000, immutable";
        try
        {
            var imageInfo = await Image.IdentifyAsync(fullPath, cancellationToken);
            if (imageInfo is not null)
            {
                var width = imageInfo.Width;
                var height = imageInfo.Height;
                if (imageInfo.Metadata.ExifProfile is { } profile
                    && profile.TryGetValue(ExifTag.Orientation, out var orientationValue)
                    && orientationValue.Value is >= 5 and <= 8)
                {
                    (width, height) = (height, width);
                }
                Response.Headers["X-Image-Width"] = width.ToString();
                Response.Headers["X-Image-Height"] = height.ToString();
            }
        }
        catch (UnknownImageFormatException) { }
        return PhysicalFile(fullPath, GetContentType(fullPath), enableRangeProcessing: true);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Download(string mode, string? userName, string? token, string path)
    {
        var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
        if (access is null) return NotFound();
        var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        return PhysicalFile(fullPath, GetContentType(fullPath), Path.GetFileName(fullPath), enableRangeProcessing: true);
    }

    [AllowAnonymous]
    public async Task DownloadFolder(string mode, string? userName, string? token, string? path, CancellationToken cancellationToken)
    {
        if (string.Equals(mode, "share", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(path))
        {
            var collectionLink = await db.ShareLinks
                .Include(x => x.Owner)
                .Include(x => x.Collection).ThenInclude(x => x!.Folders)
                .SingleOrDefaultAsync(x => x.Token == token && !x.IsRevoked && x.CollectionId != null);
            if (collectionLink?.Owner is null || collectionLink.Collection is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var collectionEntries = new List<(string FilePath, string EntryName)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in collectionLink.Collection.Folders.OrderBy(x => x.RelativePath))
            {
                var collectionFolderPath = files.ResolvePath(collectionLink.Owner, folder.RelativePath);
                if (!Directory.Exists(collectionFolderPath) || FileSystemService.IsIgnoredFileSystemEntry(collectionFolderPath)) continue;
                var baseName = new DirectoryInfo(collectionFolderPath).Name;
                var entryRoot = baseName;
                var suffix = 2;
                while (!usedNames.Add(entryRoot)) entryRoot = $"{baseName} ({suffix++})";
                collectionEntries.AddRange(EnumerateFilesWithoutReparsePoints(collectionFolderPath)
                    .Select(filePath => (filePath, $"{entryRoot}/{Path.GetRelativePath(collectionFolderPath, filePath).Replace(Path.DirectorySeparatorChar, '/')}")));
            }
            await WriteZipAsync(collectionLink.Collection.Name, collectionEntries, cancellationToken);
            return;
        }
        var access = await ResolveAccessAsync(mode, userName, token, path ?? "", requireFile: false);
        if (access is null) { Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var folderPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        if (!Directory.Exists(folderPath)) { Response.StatusCode = StatusCodes.Status404NotFound; return; }

        var downloadName = string.IsNullOrEmpty(access.Value.Path) ? access.Value.Owner.UserName ?? "gallery" : new DirectoryInfo(folderPath).Name;
        var entries = EnumerateFilesWithoutReparsePoints(folderPath)
            .Select(filePath => (FilePath: filePath, EntryName: Path.GetRelativePath(folderPath, filePath).Replace('\\', '/')));
        await WriteZipAsync(downloadName, entries, cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadSelected(
        string mode,
        string? userName,
        string? token,
        string? currentPath,
        string[] paths,
        CancellationToken cancellationToken)
    {
        var selected = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected.Count == 0) return BadRequest();

        var folderAccess = await ResolveAccessAsync(mode, userName, token, currentPath ?? "", requireFile: false);
        if (folderAccess is null) return NotFound();
        var folderPath = files.ResolvePath(folderAccess.Value.Owner, folderAccess.Value.Path);
        var normalizedFolderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedFiles = new List<string>(selected.Count);

        foreach (var path in selected)
        {
            var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
            if (access is null || access.Value.Owner.Id != folderAccess.Value.Owner.Id) return NotFound();
            var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
            var parentPath = Path.GetFullPath(Path.GetDirectoryName(fullPath)!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(parentPath, normalizedFolderPath, StringComparison.OrdinalIgnoreCase)) return NotFound();
            resolvedFiles.Add(fullPath);
        }

        if (resolvedFiles.Count == 1)
        {
            var filePath = resolvedFiles[0];
            return PhysicalFile(filePath, GetContentType(filePath), Path.GetFileName(filePath), enableRangeProcessing: true);
        }

        var zipName = $"selected-{DateTime.Now:yyyyMMdd-HHmmss}";
        await WriteZipAsync(zipName, resolvedFiles.Select(filePath => (filePath, Path.GetFileName(filePath))), cancellationToken);
        return new EmptyResult();
    }

    private async Task<IActionResult> RenderAsync(
        ApplicationUser owner,
        string? path,
        string sort,
        string dir,
        string mode,
        string? token,
        string shareRootPath,
        bool canManage,
        string? focus,
        int? initialItemsPerRow,
        string? initialViewMode,
        GalleryCollection? collection = null)
    {
        try
        {
            var normalized = files.NormalizeRelativePath(path);
            var rows = files.List(owner, normalized, sort, dir);
            IReadOnlyList<ShareLink> links = canManage
                ? (await db.ShareLinks.Where(x => x.OwnerUserId == owner.Id && x.CollectionId == null && x.RelativePath == normalized && !x.IsRevoked).ToListAsync())
                    .OrderByDescending(x => x.CreatedAtUtc).ToList()
                : [];
            IReadOnlyList<GalleryCollection> collections = canManage
                ? await db.Collections.Where(x => x.OwnerUserId == owner.Id).OrderBy(x => x.Name).ToListAsync()
                : [];
            var normalizedShareRoot = files.NormalizeRelativePath(shareRootPath).Replace('\\', '/');
            var normalizedPath = normalized.Replace('\\', '/');
            var parentPath = FileSystemService.GetParent(normalized);
            if (mode == "share" && string.Equals(normalizedPath, normalizedShareRoot, StringComparison.OrdinalIgnoreCase))
            {
                parentPath = collection is null ? null : "";
            }

            var model = new GalleryViewModel
            {
                Title = string.IsNullOrWhiteSpace(owner.DisplayName) ? owner.UserName ?? "Gallery" : owner.DisplayName,
                OwnerUserName = owner.UserName ?? "",
                Path = normalizedPath,
                ParentPath = parentPath,
                Sort = sort,
                Direction = dir,
                BrowseMode = mode,
                ShareToken = token,
                ShareRootPath = normalizedShareRoot,
                CanManage = canManage,
                FocusPath = NormalizeFocusPath(focus),
                DefaultItemsPerRow = initialItemsPerRow ?? options.Value.DefaultItemsPerRow,
                InitialItemsPerRow = initialItemsPerRow,
                InitialViewMode = initialViewMode,
                Items = rows,
                ShareLinks = links,
                Collections = collections,
                IsCollectionShare = collection is not null,
                CollectionName = collection?.Name ?? ""
            };
            return View("Index", model);
        }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return NotFound(); }
    }

    private async Task<IActionResult> RenderCollectionRootAsync(
        ShareLink link,
        string sort,
        string dir,
        int? initialItemsPerRow,
        string? initialViewMode)
    {
        var collection = link.Collection!;
        var owner = link.Owner!;
        var rows = collection.Folders
            .Select(folder => files.GetDirectoryItem(owner, folder.RelativePath))
            .Where(item => item is not null)
            .Cast<GalleryItemViewModel>();
        var descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        rows = sort switch
        {
            "date" => descending ? rows.OrderByDescending(x => x.ModifiedUtc) : rows.OrderBy(x => x.ModifiedUtc),
            _ => descending ? rows.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : rows.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return View("Index", new GalleryViewModel
        {
            Title = collection.Name,
            OwnerUserName = owner.UserName ?? "",
            Path = "",
            ParentPath = null,
            Sort = sort,
            Direction = dir,
            BrowseMode = "share",
            ShareToken = link.Token,
            ShareRootPath = "",
            CanManage = false,
            DefaultItemsPerRow = initialItemsPerRow ?? link.ItemsPerRow,
            InitialItemsPerRow = initialItemsPerRow ?? link.ItemsPerRow,
            InitialViewMode = initialViewMode ?? link.ViewMode,
            Items = rows.ToList(),
            IsCollectionShare = true,
            IsCollectionRoot = true,
            CollectionName = collection.Name
        });
    }

    private (string Sort, string Direction) ResolveSortOrder(string? requestedSort, string? requestedDirection)
    {
        var sort = NormalizeSort(requestedSort)
            ?? NormalizeSort(Request.Cookies["gallery-sort"])
            ?? "name";
        var direction = NormalizeDirection(requestedDirection)
            ?? NormalizeDirection(Request.Cookies["gallery-sort-direction"])
            ?? "asc";

        if (requestedSort is not null || requestedDirection is not null)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(365),
                Path = Request.PathBase.HasValue ? Request.PathBase.Value : "/"
            };
            Response.Cookies.Append("gallery-sort", sort, cookieOptions);
            Response.Cookies.Append("gallery-sort-direction", direction, cookieOptions);
        }

        return (sort, direction);
    }

    private static string? NormalizeSort(string? value) => value?.ToLowerInvariant() switch
    {
        "name" => "name",
        "size" => "size",
        "date" => "date",
        _ => null
    };

    private static string? NormalizeDirection(string? value) => value?.ToLowerInvariant() switch
    {
        "asc" => "asc",
        "desc" => "desc",
        _ => null
    };

    private static int? NormalizeItemsPerRow(int? value) => value is >= 2 and <= 10 ? value : null;

    private static string? NormalizeViewMode(string? value) => value?.ToLowerInvariant() switch
    {
        "grid" => "grid",
        "list" => "list",
        _ => null
    };

    private async Task<(ApplicationUser Owner, string Path)?> ResolveAccessAsync(string mode, string? userName, string? token, string path, bool requireFile)
    {
        ApplicationUser? owner;
        var normalized = files.NormalizeRelativePath(path);
        switch (mode.ToLowerInvariant())
        {
            case "private":
                if (!(User.Identity?.IsAuthenticated ?? false)) return null;
                owner = await userManager.GetUserAsync(User);
                break;
            case "share":
                var link = await db.ShareLinks
                    .Include(x => x.Owner)
                    .Include(x => x.Collection).ThenInclude(x => x!.Folders)
                    .SingleOrDefaultAsync(x => x.Token == token && !x.IsRevoked);
                if (link?.Owner is null) return null;
                if (link.Collection is null)
                {
                    if (!FileSystemService.IsWithinShareScope(link.RelativePath, normalized)) return null;
                }
                else if (FindCollectionRoot(link.Collection, normalized) is null) return null;
                owner = link.Owner;
                break;
            default: return null;
        }
        if (owner is null) return null;
        var resolved = files.ResolvePath(owner, normalized);
        if (!string.IsNullOrEmpty(normalized) && FileSystemService.IsIgnoredFileSystemEntry(resolved)) return null;
        if (requireFile ? !System.IO.File.Exists(resolved) : !Directory.Exists(resolved)) return null;
        return (owner, normalized);
    }

    private static string GetContentType(string path) => ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";
    private string NormalizeFocusPath(string? focus)
    {
        try { return files.NormalizeRelativePath(focus).Replace('\\', '/'); }
        catch (InvalidOperationException) { return ""; }
    }
    private static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    private static bool IsShareTokenFormatValid(string? token) => token is { Length: 48 } && token.All(Uri.IsHexDigit);

    private static GalleryCollectionFolder? FindCollectionRoot(GalleryCollection collection, string requestedPath) =>
        collection.Folders
            .Where(folder => FileSystemService.IsWithinShareScope(folder.RelativePath, requestedPath))
            .OrderByDescending(folder => folder.RelativePath.Length)
            .FirstOrDefault();

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            catch (ArgumentException) { continue; }
            catch (NotSupportedException) { continue; }

            foreach (var file in files)
            {
                if (!FileSystemService.IsIgnoredFileSystemEntry(file)) yield return file;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            catch (ArgumentException) { continue; }
            catch (NotSupportedException) { continue; }

            foreach (var child in children)
            {
                try
                {
                    if ((System.IO.File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0 &&
                        !FileSystemService.IsIgnoredFileSystemEntry(child)) pending.Push(child);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
        }
    }

    private async Task WriteZipAsync(string downloadName, IEnumerable<(string FilePath, string EntryName)> entries, CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(downloadName)}.zip";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var bodyControl = HttpContext.Features.Get<IHttpBodyControlFeature>();
        if (bodyControl is not null) bodyControl.AllowSynchronousIO = true;
        await Response.StartAsync(cancellationToken);
        using var archive = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (filePath, entryName) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = System.IO.File.GetLastWriteTime(filePath);
            await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = entry.Open();
            await input.CopyToAsync(output, 1024 * 128, cancellationToken);
        }
    }
}
