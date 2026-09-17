using WebGallery.Models;
using WebGallery.Services;
using WebGallery.Data;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

var root = Path.Combine(Path.GetTempPath(), "webgallery-hidden-harness-" + Guid.NewGuid().ToString("N"));
var secondRoot = Path.Combine(Path.GetTempPath(), "webgallery-second-root-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Directory.CreateDirectory(secondRoot);
var hiddenFile = Path.Combine(root, "hidden.jpg");
var systemFile = Path.Combine(root, "system.txt");
var thumbs = Path.Combine(root, "Thumbs.db");
var singularThumbs = Path.Combine(root, "Thumb.db");
var hiddenDirectory = Path.Combine(root, "System Volume Information");
var dotFile = Path.Combine(root, ".hidden-on-linux.jpg");
var outsideFile = Path.Combine(secondRoot, "outside.jpg");
var outsideLink = Path.Combine(root, "outside-link");
try
{
    var jpeg = Path.Combine(root, "decode-test.jpg");
    var png = Path.Combine(root, "decode-test.png");
    using (var fixture = new Image<Rgba32>(2400, 1600))
    {
        fixture.Metadata.ExifProfile = new ExifProfile();
        fixture.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        await fixture.SaveAsJpegAsync(jpeg);
        await fixture.SaveAsPngAsync(png);
    }
    using (var full = await ThumbnailService.DecodeAsync(jpeg, false, 480, 360, default))
    using (var reduced = await ThumbnailService.DecodeAsync(jpeg, true, 480, 360, default))
    {
        Assert(reduced.Width < full.Width, "JPEG must decode at reduced resolution");
        foreach (var img in new[] { full, reduced }) img.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Size = new Size(480, 360), Mode = ResizeMode.Max }));
        Assert(full.Size == reduced.Size && reduced.Height > reduced.Width, "reduced JPEG must preserve rotated portrait output dimensions");
    }
    using (var fallback = await ThumbnailService.DecodeAsync(png, true, 480, 360, default))
        Assert(fallback.Width == 2400, "non-JPEG must retain full decoding");
    var modes = new ThumbnailQueueSettings();
    Assert(modes.CacheVersion == "contain-v1", "default preserves old cache");
    modes.SetReducedJpeg(true);
    Assert(modes.CacheVersion == "contain-idct-v1", "reduced JPEG has separate cache");
    File.Delete(jpeg); File.Delete(png);
    await File.WriteAllTextAsync(Path.Combine(root, "visible.txt"), "visible");
    await File.WriteAllTextAsync(hiddenFile, "hidden");
    await File.WriteAllTextAsync(systemFile, "system");
    await File.WriteAllTextAsync(thumbs, "metadata");
    await File.WriteAllTextAsync(singularThumbs, "metadata");
    await File.WriteAllTextAsync(dotFile, "dotfile");
    await File.WriteAllTextAsync(outsideFile, "outside");
    Directory.CreateDirectory(hiddenDirectory);
    File.SetAttributes(hiddenFile, FileAttributes.Hidden);
    File.SetAttributes(systemFile, FileAttributes.System);
    File.SetAttributes(hiddenDirectory, FileAttributes.Hidden | FileAttributes.System);

    var owner = new ApplicationUser { Id = "harness", UserName = "harness", RootFolder = root };
    var options = new DbContextOptionsBuilder<GalleryDbContext>().UseSqlite("Data Source=:memory:").Options;
    await using var db = new GalleryDbContext(options);
    await db.Database.OpenConnectionAsync();
    await db.Database.EnsureCreatedAsync();
    var emptyOwner = new ApplicationUser { Id = "empty", UserName = "empty", RootFolder = "" };
    db.Users.AddRange(owner, emptyOwner);
    db.UserRoots.Add(new UserRoot { Id = 1, OwnerUserId = owner.Id, Name = "Fixture", PhysicalPath = root });
    db.UserRoots.Add(new UserRoot { Id = 2, OwnerUserId = owner.Id, Name = "Second", PhysicalPath = secondRoot, SortOrder = 1 });
    await db.SaveChangesAsync();
    var service = new FileSystemService(db);
    if (OperatingSystem.IsWindows())
    {
        var volumeRoot = Path.GetPathRoot(Path.GetFullPath(root))!;
        db.UserRoots.Add(new UserRoot { Id = 3, OwnerUserId = emptyOwner.Id, Name = "Volume", PhysicalPath = volumeRoot });
        await db.SaveChangesAsync();
        Assert(service.ResolvePath(emptyOwner, "@root-3") == volumeRoot, "volume root must remain absolute, not drive-relative");
        var relativeFixture = Path.GetRelativePath(volumeRoot, root);
        Assert(service.ResolvePath(emptyOwner, "@root-3/" + relativeFixture) == root, "volume child must resolve from the volume root");
        Assert(!FileSystemService.IsIgnoredFileSystemEntry(volumeRoot), "volume root Hidden/System attributes must not hide the configured volume");
        Assert(service.GetDirectoryItem(emptyOwner, "@root-3") is not null, "volume root must be available as a folder");
        db.UserRoots.Remove(await db.UserRoots.SingleAsync(r => r.Id == 3));
        await db.SaveChangesAsync();
    }
    var linkCreated = false;
    try
    {
        Directory.CreateSymbolicLink(outsideLink, secondRoot);
        linkCreated = true;
    }
    catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { }
    catch (IOException) when (OperatingSystem.IsWindows()) { }
    var home = service.List(owner, "", "name", "asc");
    Assert(home.Count == 2 && home.Select(item => item.Name).SequenceEqual(["Fixture", "Second"]), "every configured root must appear as a Gallery home folder");
    Assert(service.List(emptyOwner, "", "name", "asc").Count == 0, "a user without configured roots must receive an empty Gallery home");
    var items = service.List(owner, "@root-1", "name", "asc");
    var expectedVisibleNames = OperatingSystem.IsWindows()
        ? new[] { ".hidden-on-linux.jpg", "visible.txt" }
        : new[] { "visible.txt" };
    Assert(items.Select(item => item.Name).SequenceEqual(expectedVisibleNames), "platform hidden/system/Thumbs.db entries must be omitted");
    Assert(service.GetDirectoryItem(owner, "@root-1/System Volume Information") is null, "hidden/system directory must not become a folder card");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(thumbs), "Thumbs.db must remain ignored");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(singularThumbs), "Thumb.db must remain ignored");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(hiddenFile), "Hidden file must remain ignored");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(systemFile), "System file must remain ignored");
    Assert(FileSystemService.PathsEqual("A", "a") == OperatingSystem.IsWindows(), "path comparison must follow host filesystem casing");
    if (linkCreated)
    {
        Assert(items.All(item => item.Name != "outside-link"), "reparse-point children must not be listed");
        var escaped = false;
        try
        {
            _ = service.ResolvePath(owner, "@root-1/outside-link/outside.jpg");
        }
        catch (UnauthorizedAccessException)
        {
            escaped = true;
        }
        Assert(escaped, "a symbolic link must not resolve outside its configured root");
    }
    Console.WriteLine("Filesystem visibility self-test passed.");
}
finally
{
    File.SetAttributes(hiddenFile, FileAttributes.Normal);
    File.SetAttributes(systemFile, FileAttributes.Normal);
    File.SetAttributes(hiddenDirectory, FileAttributes.Normal);
    Directory.Delete(root, recursive: true);
    Directory.Delete(secondRoot, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
