using WebGallery.Models;
using WebGallery.ViewModels;

namespace WebGallery.Services;

public sealed class FileSystemService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif"
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
        if (string.IsNullOrWhiteSpace(owner.RootFolder))
            throw new InvalidOperationException("This user does not have a gallery root folder.");
        var root = Path.GetFullPath(owner.RootFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relative = NormalizeRelativePath(relativePath);
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(result.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The requested path is outside the gallery root.");
        return result;
    }

    public IReadOnlyList<GalleryItemViewModel> List(ApplicationUser owner, string? relativePath, string sort, string direction)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var folder = ResolvePath(owner, normalized);
        if (!Directory.Exists(folder) || (!string.IsNullOrEmpty(normalized) && IsIgnoredFileSystemEntry(folder)))
            throw new DirectoryNotFoundException();

        var items = Directory.EnumerateFileSystemEntries(folder)
            .Where(path => !IsIgnoredFileSystemEntry(path))
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
                    isDirectory ? 0 : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc,
                    extension.TrimStart('.').ToUpperInvariant(),
                    coverImages);
            });

        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        items = sort.ToLowerInvariant() switch
        {
            "size" => descending ? items.OrderByDescending(x => x.IsDirectory).ThenByDescending(x => x.Size) : items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Size),
            "date" => descending ? items.OrderByDescending(x => x.IsDirectory).ThenByDescending(x => x.ModifiedUtc) : items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.ModifiedUtc),
            _ => descending ? items.OrderByDescending(x => x.IsDirectory).ThenByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase) : items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return items.ToList();
    }

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);
    public static bool IsIgnoredFileName(string fileName) => IgnoredFileNames.Contains(fileName);

    public static bool IsHiddenOrSystem(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0;
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
            0,
            info.LastWriteTimeUtc,
            "",
            GetFolderCoverImages(fullPath, normalized));
    }

    private static IReadOnlyList<ThumbnailSourceViewModel> GetFolderCoverImages(string folderPath, string folderRelativePath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath)
                .Where(path => IsImage(Path.GetExtension(path)) && !IsIgnoredFileSystemEntry(path))
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
        var root = shareRoot.Replace('\\', '/').Trim('/');
        var requested = requestedRelativePath.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(root)
            || string.Equals(root, requested, StringComparison.OrdinalIgnoreCase)
            || requested.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }
}
