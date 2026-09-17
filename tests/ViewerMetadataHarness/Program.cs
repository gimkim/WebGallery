using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using WebGallery.Services;

using var fixture = new Image<Rgba32>(80, 40);
fixture.Metadata.ExifProfile = new ExifProfile();
fixture.Metadata.ExifProfile.SetValue(ExifTag.Model, "Test Camera");
fixture.Metadata.ExifProfile.SetValue(ExifTag.FNumber, new Rational(28, 10));
fixture.Metadata.ExifProfile.SetValue(ExifTag.ExposureTime, new Rational(1, 125));
fixture.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
using var stream = new MemoryStream();
await fixture.SaveAsJpegAsync(stream);
stream.Position = 0;
var info = await Image.IdentifyAsync(stream);
var result = ImageExifMetadata.Read(info.Metadata.ExifProfile);
if (!result.Any(v => v.Label == "Camera model" && v.Value == "Test Camera")
    || !result.Any(v => v.Value == "f/2.8") || !result.Any(v => v.Value == "1/125 s")
    || result.Count != 3 || ImageExifMetadata.Read(null).Count != 0)
    throw new Exception("EXIF round-trip, formatting, allowlist or empty metadata failed");
Console.WriteLine("PASS: JPEG header EXIF round-trip, camera/aperture/exposure, GPS omitted, absent EXIF.");
