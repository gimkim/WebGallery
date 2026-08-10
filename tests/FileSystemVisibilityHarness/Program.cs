using WebGallery.Models;
using WebGallery.Services;

var root = Path.Combine(Path.GetTempPath(), "webgallery-hidden-harness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
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

    var owner = new ApplicationUser { UserName = "harness", RootFolder = root };
    var service = new FileSystemService();
    var items = service.List(owner, "", "name", "asc");
    Assert(items.Count == 1 && items[0].Name == "visible.txt", "hidden/system/Thumbs.db entries must be omitted");
    Assert(service.GetDirectoryItem(owner, "System Volume Information") is null, "hidden/system directory must not become a folder card");
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
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
