using WebGallery.Models;
using WebGallery.ViewModels;
using WebGallery.Data;
using Microsoft.EntityFrameworkCore;

namespace WebGallery.Services;

public sealed class FileSystemService(GalleryDbContext? db = null, GalleryIndexService? index = null)
{
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif"
    };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts",
        ".3gp", ".flv", ".f4v", ".ogv", ".rmvb", ".rm", ".vob", ".asf", ".divx", ".ogm"
    };
    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db",
        // Keep the common singular spelling hidden as well; Windows normally
        // creates Thumbs.db, but copied metadata can use either name.
        "Thumb.db"
    };

    public string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return "";
        var value = Uri.UnescapeDataString(relativePath).Replace('/', Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);
        if (value.Split(Path.DirectorySeparatorChar).Any(x => x is ".." or "."))
            throw new InvalidOperationException("Invalid path.");
        return value;
    }

    public string ResolvePath(ApplicationUser owner, string? relativePath)
    {
        var relative = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(relative))
            throw new InvalidOperationException("The gallery home is a virtual folder.");

        string rootPath;
        string childPath;
        if (TryParseRootPath(relative, out var rootId, out childPath))
        {
            var configuredRoot = Database.UserRoots.AsNoTracking()
                .SingleOrDefault(root => root.Id == rootId && root.OwnerUserId == owner.Id)
                ?? throw new DirectoryNotFoundException();
            rootPath = configuredRoot.PhysicalPath;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(owner.RootFolder)) throw new DirectoryNotFoundException();
            rootPath = owner.RootFolder;
            childPath = relative;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var result = Path.GetFullPath(Path.Combine(root, childPath));
        if (!IsPathWithinRoot(root, result))
            throw new UnauthorizedAccessException("The requested path is outside the gallery root.");
        return ResolveLinksWithinRoot(root, childPath);
    }

    public IReadOnlyList<GalleryItemViewModel> List(ApplicationUser owner, string? relativePath, string sort, string direction)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalized)) return ListRoots(owner, sort, direction);
        var folder = ResolvePath(owner, normalized);
        if (!Directory.Exists(folder) || (!string.IsNullOrEmpty(normalized) && IsIgnoredFileSystemEntry(folder)))
            throw new DirectoryNotFoundException();

        IEnumerable<GalleryItemViewModel> items = index?.Read(owner, normalized) ?? Directory.EnumerateFileSystemEntries(folder)
            .Where(path => !IsReparsePoint(path) && !IsIgnoredFileSystemEntry(path))
            .Select(path =>
            {
                var isDirectory = Directory.Exists(path);
                var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
                var childRelative = string.IsNullOrEmpty(normalized) ? info.Name : Path.Combine(normalized, info.Name);
                var extension = isDirectory ? "" : Path.GetExtension(info.Name);
                var coverImages = isDirectory ? GetFolderCoverImages(path, childRelative) : [];
                return new GalleryItemViewModel(
                    info.Name,
                    childRelative.Replace(Path.DirectorySeparatorChar, '/'),
                    isDirectory,
                    !isDirectory && IsImage(extension),
                    !isDirectory && IsVideo(extension),
                    isDirectory ? 0 : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc,
                    extension.TrimStart('.').ToUpperInvariant(),
                    coverImages);
            }).ToList();

        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        items = sort.ToLowerInvariant() switch
        {
            "size" => descending ? items.OrderByDescending(x => x.IsDirectory).ThenByDescending(x => x.Size) : items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Size),
            "date" => descending ? items.OrderByDescending(x => x.IsDirectory).ThenByDescending(x => x.ModifiedUtc) : items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.ModifiedUtc),
            "taken" => SortTaken(items, descending),
            _ => descending ? items.OrderByDescending(x => x.IsDirectory).ThenByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return items.ToList();
    }

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension) || RawPreview.Extensions.Contains(extension,StringComparer.OrdinalIgnoreCase);
    public static bool HasThumbnail(string extension) => IsImage(extension) || IsVideo(extension);
    public static bool IsVideo(string extension) => VideoExtensions.Contains(extension);
    public static bool IsVirtualRoot(string? relativePath) => string.IsNullOrWhiteSpace(relativePath);
    public static bool IsIgnoredFileName(string fileName) => IgnoredFileNames.Contains(fileName)
        || System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^\.webgallery-upload-[0-9a-f]{32}\.pending$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static bool PathsEqual(string? left, string? right) => string.Equals(left, right, PathComparison);
    public static string ToLogicalPath(string value) => NormalizeScopeSeparators(value).Trim('/');

    public static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!OperatingSystem.IsWindows() && name.StartsWith(".", StringComparison.Ordinal)) return true;
            var attributes = File.GetAttributes(path);
            // Windows volume roots commonly carry Hidden/System. They are valid
            // configured roots; their children still pass the normal filters.
            var fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows()
                && PathsEqual(Path.TrimEndingDirectorySeparator(fullPath),
                    Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath)!)))
                return false;
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return true; }
        catch (ArgumentException) { return true; }
        catch (NotSupportedException) { return true; }
    }

    public static bool IsIgnoredFileSystemEntry(string path) =>
        IsHiddenOrSystem(path) || IsIgnoredFileName(Path.GetFileName(path));

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return true; }
        catch (ArgumentException) { return true; }
        catch (NotSupportedException) { return true; }
    }

    public GalleryItemViewModel? GetDirectoryItem(ApplicationUser owner, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = ResolvePath(owner, normalized);
        if (!Directory.Exists(fullPath) || IsIgnoredFileSystemEntry(fullPath)) return null;
        var info = new DirectoryInfo(fullPath);
        return new GalleryItemViewModel(
            info.Name,
            normalized.Replace(Path.DirectorySeparatorChar, '/'),
            true,
            false,
            false,
            0,
            info.LastWriteTimeUtc,
            "",
            index?.Covers(owner, normalized) ?? GetFolderCoverImages(fullPath, normalized));
    }

    public GalleryItemViewModel? GetFileItem(ApplicationUser owner, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = ResolvePath(owner, normalized);
        if (!File.Exists(fullPath) || IsIgnoredFileSystemEntry(fullPath)) return null;
        var info = new FileInfo(fullPath);
        var extension = info.Extension;
        return new GalleryItemViewModel(info.Name, normalized.Replace(Path.DirectorySeparatorChar, '/'), false,
            IsImage(extension), IsVideo(extension), info.Length, info.LastWriteTimeUtc,
            extension.TrimStart('.').ToUpperInvariant(), [], GetIndexedDateTaken(owner, normalized, info));
    }

    private DateTime? GetIndexedDateTaken(ApplicationUser owner, string path, FileInfo info)
    {
        if (index is null || !index.TryRoot(owner, path, out var root, out var relative)) return null;
        var key = GalleryIndexService.Key(relative);
        var ticks = Database.GalleryIndexEntries.AsNoTracking().Where(x => x.RootId == root.Id && x.PathKey == key
            && x.Size == info.Length && x.ModifiedTicks == info.LastWriteTimeUtc.Ticks).Select(x => x.DateTakenTicks).FirstOrDefault();
        return ticks.HasValue ? new DateTime(ticks.Value) : null;
    }

    public static IOrderedEnumerable<GalleryItemViewModel> SortTaken(IEnumerable<GalleryItemViewModel> items, bool descending)
    {
        var knownFirst = items.OrderByDescending(x => x.IsDirectory).ThenBy(x => !x.DateTaken.HasValue);
        return descending
            ? knownFirst.ThenByDescending(x => x.DateTaken?.Ticks ?? x.ModifiedUtc.UtcTicks).ThenByDescending(x => x.Name, PathComparer)
            : knownFirst.ThenBy(x => x.DateTaken?.Ticks ?? x.ModifiedUtc.UtcTicks).ThenBy(x => x.Name, PathComparer);
    }

    public string GetPathDisplayName(ApplicationUser owner, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (!TryParseRootPath(normalized, out var rootId, out var childPath))
            return string.IsNullOrEmpty(normalized) ? "Home" : normalized.Split(Path.DirectorySeparatorChar).Last();
        if (!string.IsNullOrEmpty(childPath)) return childPath.Split(Path.DirectorySeparatorChar).Last();
        return Database.UserRoots.AsNoTracking().Where(root => root.Id == rootId && root.OwnerUserId == owner.Id)
            .Select(root => root.Name).SingleOrDefault() ?? "Folder";
    }

    public IReadOnlyList<(string Path, string Name)> GetBreadcrumbs(ApplicationUser owner, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var result = new List<(string Path, string Name)> { ("", "Home") };
        if (string.IsNullOrEmpty(normalized)) return result;
        if (!TryParseRootPath(normalized, out var rootId, out var childPath))
        {
            var path = "";
            foreach (var segment in normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                path = string.IsNullOrEmpty(path) ? segment : Path.Combine(path, segment);
                result.Add((path.Replace(Path.DirectorySeparatorChar, '/'), segment));
            }
            return result;
        }
        var marker = RootMarker(rootId);
        result.Clear();
        var rootName = Database.UserRoots.AsNoTracking().Where(root => root.Id == rootId && root.OwnerUserId == owner.Id)
            .Select(root => root.Name).SingleOrDefault() ?? "Folder";
        result.Add((marker, rootName));
        var current = marker;
        foreach (var segment in childPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            result.Add((current, segment));
        }
        return result;
    }

    public static string RootMarker(int rootId) => $"@root-{rootId}";

    private IReadOnlyList<GalleryItemViewModel> ListRoots(ApplicationUser owner, string sort, string direction)
    {
        IEnumerable<GalleryItemViewModel> items = Database.UserRoots.AsNoTracking()
            .Where(root => root.OwnerUserId == owner.Id)
            .OrderBy(root => root.SortOrder).ThenBy(root => root.Id)
            .AsEnumerable()
            .Select(root =>
            {
                var marker = RootMarker(root.Id);
                var available = Directory.Exists(root.PhysicalPath) && !IsIgnoredFileSystemEntry(root.PhysicalPath);
                var modified = available ? new DirectoryInfo(root.PhysicalPath).LastWriteTimeUtc : root.CreatedAtUtc;
                return new GalleryItemViewModel(root.Name, marker, true, false, false, 0,
                    modified, "", available ? index?.Covers(owner, marker) ?? GetFolderCoverImages(root.PhysicalPath, marker) : []);
            });
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        items = sort.ToLowerInvariant() switch
        {
            "date" => descending ? items.OrderByDescending(item => item.ModifiedUtc) : items.OrderBy(item => item.ModifiedUtc),
            _ => descending ? items.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase) : items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return items.ToList();
    }

    private static bool TryParseRootPath(string relativePath, out int rootId, out string childPath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, 2, StringSplitOptions.RemoveEmptyEntries);
        var marker = segments.FirstOrDefault() ?? "";
        if (!marker.StartsWith("@root-", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(marker[6..], out rootId) || rootId <= 0)
        {
            rootId = 0;
            childPath = "";
            return false;
        }
        childPath = segments.Length > 1 ? segments[1] : "";
        return true;
    }

    private GalleryDbContext Database => db ?? throw new InvalidOperationException("A database context is required for configured gallery roots.");

    private static IReadOnlyList<ThumbnailSourceViewModel> GetFolderCoverImages(string folderPath, string folderRelativePath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath)
                .Where(path => HasThumbnail(Path.GetExtension(path)) && !IsReparsePoint(path) && !IsIgnoredFileSystemEntry(path))
                .Take(4)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    var relativePath = Path.Combine(folderRelativePath, info.Name).Replace(Path.DirectorySeparatorChar, '/');
                    return new ThumbnailSourceViewModel(relativePath, CreateThumbnailCacheStamp(info.Length, info.LastWriteTimeUtc.Ticks));
                })
                .ToList();
        }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
    }

    public static string CreateThumbnailCacheStamp(long length, long modifiedUtcTicks) => $"{modifiedUtcTicks:x}-{length:x}";

    public static string? GetParent(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath)) return null;
        var value = normalizedPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetDirectoryName(value)?.Replace(Path.DirectorySeparatorChar, '/') ?? "";
    }

    public static bool IsWithinShareScope(string shareRoot, string requestedRelativePath)
    {
        var root = NormalizeScopeSeparators(shareRoot).Trim('/');
        var requested = NormalizeScopeSeparators(requestedRelativePath).Trim('/');
        return string.IsNullOrEmpty(root)
            || string.Equals(root, requested, PathComparison)
            || requested.StartsWith(root + "/", PathComparison);
    }

    private static string NormalizeScopeSeparators(string value) => OperatingSystem.IsWindows()
        ? value.Replace('\\', '/')
        : value;

    private static bool IsPathWithinRoot(string root, string candidate)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PathsEqual(normalizedRoot, normalizedCandidate)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static string ResolveLinksWithinRoot(string root, string childPath)
    {
        var canonicalRoot = ResolveLinkTarget(root);
        var current = canonicalRoot;
        foreach (var segment in childPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            next = ResolveLinkTarget(next);
            if (!IsPathWithinRoot(canonicalRoot, next))
                throw new UnauthorizedAccessException("The requested path resolves outside the gallery root.");
            current = next;
        }
        return Path.GetFullPath(current);
    }

    private static string ResolveLinkTarget(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (string.IsNullOrEmpty(info.LinkTarget)) return Path.GetFullPath(path);
        var target = info.ResolveLinkTarget(returnFinalTarget: true)
            ?? throw new UnauthorizedAccessException("The requested symbolic link cannot be resolved.");
        return Path.GetFullPath(target.FullName);
    }
}
