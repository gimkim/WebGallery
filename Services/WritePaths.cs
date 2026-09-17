using System.Text;
using System.Text.RegularExpressions;
using WebGallery.Models;

namespace WebGallery.Services;

// A deliberately portable namespace: do not accept Windows aliases on Linux either.
public static class WritePaths
{
    public static string Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 || Encoding.UTF8.GetByteCount(value) > 240
            || value != value.Trim() || value.StartsWith('.') || value.EndsWith('.')
            || value.Any(c => char.IsControl(c) || "<>:\"/\\|?*%".Contains(c))
            || Regex.IsMatch(value.Split('.')[0].TrimEnd(' '), @"^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³])$", RegexOptions.IgnoreCase)
            || FileSystemService.IsIgnoredFileName(value)
            || value.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid name. Use 1–240 portable characters; reserved names, hidden names, trailing spaces/dots and path separators are not allowed.");
        return value;
    }

    public static string Relative(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.StartsWith('/') || value.Contains('\\'))
            throw new InvalidOperationException("Invalid relative path.");
        return string.Join('/', value.Split('/').Select(Name));
    }

    public static string Resolve(UserRoot root, string logical, bool directory = true)
    {
        var marker = FileSystemService.RootMarker(root.Id);
        if (logical != marker && !logical.StartsWith(marker + "/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The destination is outside your root folder.");
        var suffix = logical.Length == marker.Length ? "" : Relative(logical[(marker.Length + 1)..]);
        var current = Path.GetFullPath(root.PhysicalPath);
        Check(current);
        foreach (var part in suffix.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current)) Check(current);
            else if (new FileInfo(current).LinkTarget is not null) throw new UnauthorizedAccessException("Symbolic links are not writable targets.");
        }
        if (directory && !Directory.Exists(current)) throw new DirectoryNotFoundException("The destination folder no longer exists.");
        return current;
    }

    private static void Check(string path)
    {
        if (FileSystemService.IsReparsePoint(path) || FileSystemService.IsIgnoredFileSystemEntry(path))
            throw new UnauthorizedAccessException("Hidden, system and linked paths are not writable targets.");
    }
}
