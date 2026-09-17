using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WebGallery.ViewModels;

namespace WebGallery.Services;

public sealed record GalleryBrowseSection(string Key, string Label, int Offset, int Count);
public static class GalleryBrowseSections
{
    public static (string Key, string Label) Group(GalleryItemViewModel item, string sort)
    {
        var prefix = item.IsDirectory ? "folder-" : "file-";
        var labelPrefix = item.IsDirectory ? "Folders · " : "";
        if (sort is "date" or "taken") {
            var taken = sort == "taken" && item.DateTaken.HasValue;
            var date = taken ? item.DateTaken!.Value : item.ModifiedUtc.ToLocalTime().DateTime;
            var fallback = sort == "taken" && !taken;
            return (prefix + (fallback ? "modified-" : "") + date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                labelPrefix + date.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + (fallback ? " · Date Modified" : ""));
        }
        if (sort == "size") {
            var bucket = item.Size < 1024 * 1024 ? "Under 1 MB" : item.Size < 10 * 1024 * 1024 ? "1–10 MB"
                : item.Size < 100 * 1024 * 1024 ? "10–100 MB" : item.Size < 1024L * 1024 * 1024 ? "100 MB–1 GB" : "1 GB+";
            return (prefix + bucket, labelPrefix + bucket);
        }
        var first = string.IsNullOrEmpty(item.Name) ? "#" : StringInfo.GetNextTextElement(item.Name).ToUpperInvariant();
        if (!char.IsLetterOrDigit(first, 0)) first = "#";
        return (prefix + first, labelPrefix + first);
    }
    public static IReadOnlyList<GalleryBrowseSection> Create(IReadOnlyList<GalleryItemViewModel> items, string sort)
    {
        var result = new List<GalleryBrowseSection>();
        for (var i = 0; i < items.Count; i++) {
            var group = Group(items[i], sort);
            if (result.Count > 0 && result[^1].Key == group.Key) result[^1] = result[^1] with { Count = result[^1].Count + 1 };
            else result.Add(new(group.Key, group.Label, i, 1));
        }
        return result;
    }
    public static string Revision(IEnumerable<GalleryItemViewModel> items, string sort)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var item in items) hash.AppendData(Encoding.UTF8.GetBytes($"{item.RelativePath.Length}:{item.RelativePath}|{item.Size}|{item.ModifiedUtc.UtcTicks}|{(sort == "taken" ? item.DateTaken?.Ticks : null)}\n"));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
