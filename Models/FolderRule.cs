namespace WebGallery.Models;

public sealed class FolderRule
{
    public int Id { get; set; }
    public required string OwnerUserId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public string RelativePath { get; set; } = "";
    public FolderAccessMode AccessMode { get; set; } = FolderAccessMode.Private;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
