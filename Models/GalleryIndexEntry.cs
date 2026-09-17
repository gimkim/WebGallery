namespace WebGallery.Models;

public sealed class GalleryIndexEntry
{
    public int RootId { get; set; }
    public string PathKey { get; set; } = "";
    public string ParentKey { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string PhysicalKey { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsImage { get; set; }
    public bool IsVideo { get; set; }
    public string Extension { get; set; } = "";
    public long Size { get; set; }
    public long ModifiedTicks { get; set; }
    public long? DateTakenTicks { get; set; }
    public long DateTakenScanAfter { get; set; }
    public string ThumbnailSignature { get; set; } = "";
    public long RetryAfter { get; set; }
    public string MediumThumbnailSignature { get; set; } = "";
    public long MediumRetryAfter { get; set; }
}

public sealed class GalleryIndexFolder
{
    public int RootId { get; set; }
    public string PathKey { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string PhysicalRoot { get; set; } = "";
    public long ScannedAt { get; set; }
    public long NextScan { get; set; }
    public string Revision { get; set; } = "";
    public string Error { get; set; } = "";
}
