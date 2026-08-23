using WebGallery.Models;
using WebGallery.Services;
using WebGallery.Data;
using Microsoft.EntityFrameworkCore;

var root = Path.Combine(Path.GetTempPath(), "webgallery-hidden-harness-" + Guid.NewGuid().ToString("N"));
var secondRoot = Path.Combine(Path.GetTempPath(), "webgallery-second-root-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Directory.CreateDirectory(secondRoot);
var hiddenFile = Path.Combine(root, "hidden.jpg");
var systemFile = Path.Combine(root, "system.txt");
var thumbs = Path.Combine(root, "Thumbs.db");
var singularThumbs = Path.Combine(root, "Thumb.db");
var hiddenDirectory = Path.Combine(root, "System Volume Information");
try
{
    await File.WriteAllTextAsync(Path.Combine(root, "visible.txt"), "visible");
    await File.WriteAllTextAsync(hiddenFile, "hidden");
    await File.WriteAllTextAsync(systemFile, "system");
    await File.WriteAllTextAsync(thumbs, "metadata");
    await File.WriteAllTextAsync(singularThumbs, "metadata");
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
    var home = service.List(owner, "", "name", "asc");
    Assert(home.Count == 2 && home.Select(item => item.Name).SequenceEqual(["Fixture", "Second"]), "every configured root must appear as a Gallery home folder");
    Assert(service.List(emptyOwner, "", "name", "asc").Count == 0, "a user without configured roots must receive an empty Gallery home");
    var items = service.List(owner, "@root-1", "name", "asc");
    Assert(items.Count == 1 && items[0].Name == "visible.txt", "hidden/system/Thumbs.db entries must be omitted");
    Assert(service.GetDirectoryItem(owner, "@root-1/System Volume Information") is null, "hidden/system directory must not become a folder card");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(thumbs), "Thumbs.db must remain ignored");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(singularThumbs), "Thumb.db must remain ignored");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(hiddenFile), "Hidden file must remain ignored");
    Assert(FileSystemService.IsIgnoredFileSystemEntry(systemFile), "System file must remain ignored");
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
