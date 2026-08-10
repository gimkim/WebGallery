namespace WebGallery.Models;

public sealed class ShareLink
{
    public int Id { get; set; }
    public required string Token { get; set; }
    public required string OwnerUserId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public string RelativePath { get; set; } = "";
    public int? CollectionId { get; set; }
    public GalleryCollection? Collection { get; set; }
    public string Sort { get; set; } = "name";
    public string Direction { get; set; } = "asc";
    public int ItemsPerRow { get; set; } = 8;
    public string ViewMode { get; set; } = "grid";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsRevoked { get; set; }
}
