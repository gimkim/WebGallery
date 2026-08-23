using System.IO.Compression;
using System.Security.Cryptography;
using System.Globalization;
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
    MediaService media,
    ShareAuditService shareAudit,
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
        if (link.TargetType == ShareTargetTypes.File)
        {
            if (!string.IsNullOrWhiteSpace(path)) return NotFound();
            var fileResult = await RenderFileShareAsync(link);
            if (fileResult is ViewResult) await shareAudit.RecordAsync(link, ShareAuditEventTypes.Access, link.RelativePath);
            return fileResult;
        }
        if (link.Collection is not null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var rootResult = await RenderCollectionRootAsync(link, order.Sort, order.Direction,
                    NormalizeItemsPerRow(itemsPerRow), NormalizeViewMode(view));
                if (rootResult is ViewResult)
                    await shareAudit.RecordAsync(link, ShareAuditEventTypes.Access, "");
                return rootResult;
            }
            var requested = files.NormalizeRelativePath(path).Replace(Path.DirectorySeparatorChar, '/');
            var collectionRoot = FindCollectionRoot(link.Collection, requested);
            if (collectionRoot is null) return NotFound();
            var collectionPathResult = await RenderAsync(link.Owner, requested, order.Sort, order.Direction, "share", token,
                collectionRoot.RelativePath, canManage: false, focus, NormalizeItemsPerRow(itemsPerRow),
                NormalizeViewMode(view), link.Collection);
            if (collectionPathResult is ViewResult)
                await shareAudit.RecordAsync(link, ShareAuditEventTypes.Access, requested);
            return collectionPathResult;
        }
        var normalized = string.IsNullOrEmpty(path) ? link.RelativePath : files.NormalizeRelativePath(path);
        if (!FileSystemService.IsWithinShareScope(link.RelativePath, normalized)) return NotFound();
        var folderResult = await RenderAsync(link.Owner, normalized, order.Sort, order.Direction, "share", token, link.RelativePath,
            canManage: false, focus, NormalizeItemsPerRow(itemsPerRow), NormalizeViewMode(view));
        if (folderResult is ViewResult)
            await shareAudit.RecordAsync(link, ShareAuditEventTypes.Access, normalized);
        return folderResult;
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
        if (string.IsNullOrEmpty(normalized)) return BadRequest();
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
            ViewMode = NormalizeViewMode(viewMode) ?? "grid",
            TargetType = ShareTargetTypes.Folder
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
    public async Task<IActionResult> CreateFileShare(string? currentPath, string[] paths)
    {
        var owner = await userManager.GetUserAsync(User);
        if (owner is null) return Challenge();
        var selected = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected.Count != 1)
        {
            TempData["Error"] = "Select exactly one image or video to create a file share link.";
            return RedirectToAction(nameof(Index), new { path = currentPath });
        }
        var normalized = files.NormalizeRelativePath(selected[0]).Replace(Path.DirectorySeparatorChar, '/');
        var item = files.GetFileItem(owner, normalized);
        if (item is null || (!item.IsImage && !item.IsVideo)) return NotFound();
        var normalizedCurrent = files.NormalizeRelativePath(currentPath).Replace(Path.DirectorySeparatorChar, '/');
        if (!string.Equals(FileSystemService.GetParent(normalized), normalizedCurrent, StringComparison.OrdinalIgnoreCase)) return NotFound();
        var link = new ShareLink
        {
            OwnerUserId = owner.Id,
            RelativePath = normalized,
            Token = CreateToken(),
            TargetType = ShareTargetTypes.File,
            Sort = "name",
            Direction = "asc",
            ItemsPerRow = options.Value.DefaultItemsPerRow,
            ViewMode = "grid"
        };
        db.ShareLinks.Add(link);
        await db.SaveChangesAsync();
        TempData["Success"] = "File share link created.";
        TempData["OpenSharePanel"] = true;
        TempData["CreatedFileShareLinkId"] = link.Id;
        return RedirectToAction(nameof(Index), new { path = normalizedCurrent });
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
    public async Task<IActionResult> MediaMetadata(string mode, string? userName, string? token, string path, CancellationToken cancellationToken)
    {
        var access = await ResolveVideoAccessAsync(mode, userName, token, path);
        if (access is null) return NotFound();
        try
        {
            var result = await media.GetMetadataAsync(access.Value.FullPath, cancellationToken);
            if (access.Value.Access.ShareLink is not null)
                await shareAudit.RecordAsync(access.Value.Access.ShareLink, ShareAuditEventTypes.View, access.Value.Access.Path);
            Response.Headers.CacheControl = "private, no-store";
            return Ok(result);
        }
        catch (MediaPlaybackException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }

    [AllowAnonymous]
    public async Task<IActionResult> MediaSubtitle(string mode, string? userName, string? token, string path, int? subtitle, string? progress, CancellationToken cancellationToken)
    {
        if (!subtitle.HasValue) return BadRequest(new { message = "A subtitle track is required." });
        var access = await ResolveVideoAccessAsync(mode, userName, token, path);
        if (access is null) return NotFound();
        try
        {
            var bytes = await media.GetSubtitleAsync(access.Value.FullPath, subtitle.Value, progress, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(bytes, "text/vtt; charset=utf-8");
        }
        catch (MediaPlaybackException ex) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }

    [AllowAnonymous]
    public async Task<IActionResult> MediaSubtitleProgress(string mode, string? userName, string? token, string path, string? id)
    {
        if (await ResolveVideoAccessAsync(mode, userName, token, path) is null) return NotFound();
        var progress = media.GetSubtitleProgress(id);
        return progress is null ? NotFound() : Ok(progress);
    }

    [AllowAnonymous]
    public async Task<IActionResult> MediaSegment(string mode, string? userName, string? token, string path, int? audio, int? subtitle, double? start, double? duration, bool? hevc, CancellationToken cancellationToken)
    {
        var access = await ResolveVideoAccessAsync(mode, userName, token, path);
        if (access is null) return NotFound();
        try
        {
            var segment = await media.GetSegmentAsync(access.Value.FullPath, audio, subtitle, start ?? -1, duration ?? -1, cancellationToken, allowHevc: hevc == true);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["X-Media-Mode"] = segment.Mode;
            Response.Headers["X-Media-Output-Codec"] = segment.OutputCodec;
            if (segment.Profile is not null) Response.Headers["X-Media-Profile"] = segment.Profile;
            if (segment.Level is not null) Response.Headers["X-Media-Level"] = segment.Level;
            if (segment.Quality is not null) Response.Headers["X-Media-Quality"] = segment.Quality;
            if (segment.TargetBitRate.HasValue) Response.Headers["X-Media-Target-Kbps"] = (segment.TargetBitRate.Value / 1000).ToString(CultureInfo.InvariantCulture);
            if (segment.MaxBitRate.HasValue) Response.Headers["X-Media-Max-Kbps"] = (segment.MaxBitRate.Value / 1000).ToString(CultureInfo.InvariantCulture);
            Response.Headers["X-Media-Source-Start"] = segment.SourceStart.ToString("0.###", CultureInfo.InvariantCulture);
            Response.Headers["X-Media-Presentation-Lead"] = segment.PresentationLead.ToString("0.###", CultureInfo.InvariantCulture);
            Response.Headers["X-Media-Next-Start"] = segment.NextStart.ToString("0.###", CultureInfo.InvariantCulture);
            return File(segment.Bytes, "video/mp4");
        }
        catch (MediaPlaybackException ex) when (!Response.HasStarted) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }

    [AllowAnonymous]
    public async Task<IActionResult> MediaStream(string mode, string? userName, string? token, string path, int? audio, int? subtitle, CancellationToken cancellationToken)
    {
        var access = await ResolveVideoAccessAsync(mode, userName, token, path);
        if (access is null) return NotFound();
        try { await media.StreamAsync(HttpContext, access.Value.FullPath, audio, subtitle, cancellationToken); return new EmptyResult(); }
        catch (MediaPlaybackException ex) when (!Response.HasStarted) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }

    [AllowAnonymous]
    public async Task<IActionResult> MediaContinuousHevc(string mode, string? userName, string? token, string path, int? audio, double? start, CancellationToken cancellationToken)
    {
        var access = await ResolveVideoAccessAsync(mode, userName, token, path);
        if (access is null) return NotFound();
        try { await media.StreamContinuousHevcAsync(HttpContext, access.Value.FullPath, audio, start ?? 0, cancellationToken); return new EmptyResult(); }
        catch (MediaPlaybackException ex) when (!Response.HasStarted) { return StatusCode(ex.StatusCode, new { message = ex.Message }); }
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> AuditView(string? token, string path)
    {
        var clientAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cooldown = invalidShareTokenLimiter.GetCooldown(clientAddress);
        if (cooldown.IsActive) return ShareCooldown(cooldown);
        if (!IsShareTokenFormatValid(token)) return InvalidShareToken(clientAddress);

        var access = await ResolveAccessAsync("share", null, token, path, requireFile: true);
        if (access?.ShareLink is null) return NotFound();
        var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        if (!FileSystemService.IsImage(Path.GetExtension(fullPath))) return NotFound();
        await shareAudit.RecordAsync(access.Value.ShareLink, ShareAuditEventTypes.View, access.Value.Path);
        return NoContent();
    }

    [AllowAnonymous]
    public async Task<IActionResult> Download(string mode, string? userName, string? token, string path)
    {
        var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
        if (access is null) return NotFound();
        var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        if (access.Value.ShareLink is not null)
            await shareAudit.RecordAsync(access.Value.ShareLink, ShareAuditEventTypes.DownloadFile, access.Value.Path);
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
            await shareAudit.RecordAsync(collectionLink, ShareAuditEventTypes.DownloadCollection,
                collectionLink.Collection.Name, collectionEntries.Count);
            await WriteZipAsync(collectionLink.Collection.Name, collectionEntries, cancellationToken);
            return;
        }
        var access = await ResolveAccessAsync(mode, userName, token, path ?? "", requireFile: false);
        if (access is null) { Response.StatusCode = StatusCodes.Status404NotFound; return; }
        var folderPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        if (!Directory.Exists(folderPath)) { Response.StatusCode = StatusCodes.Status404NotFound; return; }

        var downloadName = string.IsNullOrEmpty(access.Value.Path) ? access.Value.Owner.UserName ?? "gallery" : new DirectoryInfo(folderPath).Name;
        var entries = EnumerateFilesWithoutReparsePoints(folderPath)
            .Select(filePath => (FilePath: filePath, EntryName: Path.GetRelativePath(folderPath, filePath).Replace('\\', '/')))
            .ToList();
        if (access.Value.ShareLink is not null)
            await shareAudit.RecordAsync(access.Value.ShareLink, ShareAuditEventTypes.DownloadFolder,
                access.Value.Path, entries.Count);
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
        var resolvedRelativePaths = new List<string>(selected.Count);

        foreach (var path in selected)
        {
            var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
            if (access is null || access.Value.Owner.Id != folderAccess.Value.Owner.Id) return NotFound();
            var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
            var parentPath = Path.GetFullPath(Path.GetDirectoryName(fullPath)!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(parentPath, normalizedFolderPath, StringComparison.OrdinalIgnoreCase)) return NotFound();
            resolvedFiles.Add(fullPath);
            resolvedRelativePaths.Add(access.Value.Path);
        }

        if (resolvedFiles.Count == 1)
        {
            var filePath = resolvedFiles[0];
            if (folderAccess.Value.ShareLink is not null)
                await shareAudit.RecordAsync(folderAccess.Value.ShareLink, ShareAuditEventTypes.DownloadSelection,
                    resolvedRelativePaths[0], detailPaths: resolvedRelativePaths);
            return PhysicalFile(filePath, GetContentType(filePath), Path.GetFileName(filePath), enableRangeProcessing: true);
        }

        var zipName = $"selected-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (folderAccess.Value.ShareLink is not null)
            await shareAudit.RecordAsync(folderAccess.Value.ShareLink, ShareAuditEventTypes.DownloadSelection,
                folderAccess.Value.Path, resolvedFiles.Count, resolvedRelativePaths);
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
                ? (await db.ShareLinks.Where(x => x.OwnerUserId == owner.Id && x.CollectionId == null && x.TargetType == ShareTargetTypes.Folder && x.RelativePath == normalized && !x.IsRevoked).ToListAsync())
                    .OrderByDescending(x => x.CreatedAtUtc).ToList()
                : [];
            IReadOnlyList<ShareLink> fileLinks = canManage
                ? (await db.ShareLinks.Where(x => x.OwnerUserId == owner.Id && x.TargetType == ShareTargetTypes.File && !x.IsRevoked).ToListAsync())
                    .Where(link => string.Equals(FileSystemService.GetParent(link.RelativePath), normalized.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(link => link.CreatedAtUtc).ToList()
                : [];
            var shareSummaries = await shareAudit.GetSummariesAsync(links.Concat(fileLinks).Select(link => link.Id));
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
                ShareLinks = links.Select(link => CreateShareManagementModel(link, shareSummaries)).ToList(),
                FileShareLinks = fileLinks.Select(link => CreateShareManagementModel(link, shareSummaries)).ToList(),
                IsCollectionShare = collection is not null,
                CollectionName = collection?.Name ?? "",
                IsVirtualRoot = mode == "private" && FileSystemService.IsVirtualRoot(normalized),
                Breadcrumbs = mode == "private"
                    ? files.GetBreadcrumbs(owner, normalized).Select(item => new GalleryBreadcrumbViewModel(item.Path, item.Name)).ToList()
                    : []
            };
            return View("Index", model);
        }
        catch (DirectoryNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return NotFound(); }
    }

    private IActionResult RenderFileShare(ShareLink link, GalleryItemViewModel item) => View("Index", new GalleryViewModel
    {
        Title = item.Name,
        OwnerUserName = link.Owner?.UserName ?? "",
        Path = item.RelativePath,
        ParentPath = null,
        BrowseMode = "share",
        ShareToken = link.Token,
        ShareRootPath = item.RelativePath,
        CanManage = false,
        DefaultItemsPerRow = 2,
        InitialItemsPerRow = 2,
        InitialViewMode = "grid",
        Items = [item],
        IsFileShare = true,
        Breadcrumbs = [new GalleryBreadcrumbViewModel(item.RelativePath, item.Name)]
    });

    private Task<IActionResult> RenderFileShareAsync(ShareLink link)
    {
        var item = link.Owner is null ? null : files.GetFileItem(link.Owner, link.RelativePath);
        return Task.FromResult(item is null || (!item.IsImage && !item.IsVideo) ? (IActionResult)NotFound() : RenderFileShare(link, item));
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

    private static ShareLinkManagementViewModel CreateShareManagementModel(
        ShareLink link,
        IReadOnlyDictionary<int, ShareAuditSummary> summaries)
    {
        summaries.TryGetValue(link.Id, out var summary);
        summary ??= new ShareAuditSummary(0, 0, 0, 0, null);
        return new ShareLinkManagementViewModel
        {
            Link = link,
            AccessCount = summary.AccessCount,
            ViewCount = summary.ViewCount,
            DownloadCount = summary.DownloadCount,
            UniqueVisitorCount = summary.UniqueVisitorCount,
            LastActivityUtc = summary.LastActivityUtc
        };
    }

    private async Task<ResolvedAccess?> ResolveAccessAsync(string mode, string? userName, string? token, string path, bool requireFile)
    {
        ApplicationUser? owner;
        ShareLink? shareLink = null;
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
                shareLink = link;
                if (link.Collection is null)
                {
                    if (link.TargetType == ShareTargetTypes.File)
                    {
                        if (!string.Equals(link.RelativePath.Replace('\\', '/'), normalized.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) return null;
                    }
                    else if (!FileSystemService.IsWithinShareScope(link.RelativePath, normalized)) return null;
                }
                else if (FindCollectionRoot(link.Collection, normalized) is null) return null;
                owner = link.Owner;
                break;
            default: return null;
        }
        if (owner is null) return null;
        if (string.IsNullOrEmpty(normalized)) return null;
        var resolved = files.ResolvePath(owner, normalized);
        if (!string.IsNullOrEmpty(normalized) && FileSystemService.IsIgnoredFileSystemEntry(resolved)) return null;
        if (requireFile ? !System.IO.File.Exists(resolved) : !Directory.Exists(resolved)) return null;
        return new ResolvedAccess(owner, normalized, shareLink);
    }

    private async Task<ResolvedVideoAccess?> ResolveVideoAccessAsync(string mode, string? userName, string? token, string path)
    {
        var access = await ResolveAccessAsync(mode, userName, token, path, requireFile: true);
        if (access is null) return null;
        var fullPath = files.ResolvePath(access.Value.Owner, access.Value.Path);
        return FileSystemService.IsVideo(Path.GetExtension(fullPath))
            ? new ResolvedVideoAccess(access.Value, fullPath)
            : null;
    }

    private static string GetContentType(string path) => ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";
    private string NormalizeFocusPath(string? focus)
    {
        try { return files.NormalizeRelativePath(focus).Replace('\\', '/'); }
        catch (InvalidOperationException) { return ""; }
    }
    private static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    private static bool IsShareTokenFormatValid(string? token) => token is { Length: 48 } && token.All(Uri.IsHexDigit);

    private readonly record struct ResolvedAccess(ApplicationUser Owner, string Path, ShareLink? ShareLink);
    private readonly record struct ResolvedVideoAccess(ResolvedAccess Access, string FullPath);

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
