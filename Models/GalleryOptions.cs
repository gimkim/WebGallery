using WebGallery.Services;

namespace WebGallery.Models;

public sealed class GalleryOptions
{
    public string AppTitle { get; set; } = "Gallery";
    public string CachePath { get; set; } = "App_Data/cache";
    public string DataProtectionKeysPath { get; set; } = "App_Data/keys";
    public string DefaultRootPath { get; set; } = "App_Data/gallery-content";
    public int ThumbnailWidth { get; set; } = 480;
    public int ThumbnailHeight { get; set; } = 360;
    public int ThumbnailQuality { get; set; } = 78;
    public int ThumbnailConcurrency { get; set; } = ThumbnailQueueSettings.DefaultConcurrency;
    public int DefaultItemsPerRow { get; set; } = 8;
}
