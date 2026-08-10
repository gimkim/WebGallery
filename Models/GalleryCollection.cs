namespace WebGallery.Models;

public sealed class GalleryCollection
{
    public int Id { get; set; }
    public required string OwnerUserId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<GalleryCollectionFolder> Folders { get; set; } = [];
    public List<ShareLink> ShareLinks { get; set; } = [];
}

public sealed class GalleryCollectionFolder
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public GalleryCollection? Collection { get; set; }
    public required string RelativePath { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
