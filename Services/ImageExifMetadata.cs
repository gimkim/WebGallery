using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace WebGallery.Services;

public static class ImageExifMetadata
{
    // Deliberately omit GPS, serial numbers and free-form private maker notes.
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Make"] = "Camera make", ["Model"] = "Camera model", ["LensModel"] = "Lens",
        ["DateTimeOriginal"] = "Date taken", ["ExposureTime"] = "Exposure",
        ["FNumber"] = "Aperture", ["ISOSpeedRatings"] = "ISO", ["PhotographicSensitivity"] = "ISO",
        ["FocalLength"] = "Focal length", ["FocalLengthIn35mmFilm"] = "35mm equivalent",
        ["Software"] = "Software"
    };

    public static IReadOnlyList<Entry> Read(ExifProfile? profile)
    {
        var result = new List<Entry>();
        if (profile is null) return result;
        foreach (var entry in profile.Values)
        {
            var tag = entry.Tag.ToString();
            if (!Labels.TryGetValue(tag, out var label)) continue;
            var raw = entry.GetValue();
            var value = raw switch
            {
                Rational r when tag == "ExposureTime" => r.Numerator == 1 ? $"1/{r.Denominator} s" : $"{r.ToDouble().ToString("0.####", CultureInfo.InvariantCulture)} s",
                Rational r when tag == "FNumber" => $"f/{r.ToDouble().ToString("0.#", CultureInfo.InvariantCulture)}",
                Rational r when tag == "FocalLength" => $"{r.ToDouble().ToString("0.#", CultureInfo.InvariantCulture)} mm",
                Array a => string.Join(", ", a.Cast<object>().Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))),
                _ => Convert.ToString(raw, CultureInfo.InvariantCulture)
            };
            value = value?.Trim('\0', ' ', '\r', '\n');
            if (!string.IsNullOrEmpty(value)) result.Add(new(label, value.Length > 256 ? value[..256] : value));
        }
        return result;
    }

    public sealed record Entry(string Label, string Value);
}
