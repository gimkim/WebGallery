namespace WebGallery.Models;

public sealed class UserRoot
{
    public int Id { get; set; }
    public required string OwnerUserId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public required string Name { get; set; }
    public required string PhysicalPath { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
