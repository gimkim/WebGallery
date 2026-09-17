namespace WebGallery.Models;

public sealed class FolderBrowsePreference
{
    public required string OwnerId { get; set; }
    public required string FolderKey { get; set; }
    public string Sort { get; set; } = "name";
    public string Direction { get; set; } = "asc";
}
