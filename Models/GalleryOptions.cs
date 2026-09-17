using WebGallery.Services;

namespace WebGallery.Models;

public sealed class GalleryOptions
{
    public string AppTitle { get; set; } = "Gallery";
    public string ExifToolPath { get; set; } = "exiftool";
    public string CachePath { get; set; } = "App_Data/cache";
    public string DataProtectionKeysPath { get; set; } = "App_Data/keys";
    public string DefaultRootPath { get; set; } = "App_Data/gallery-content";
    public int ThumbnailWidth { get; set; } = 480;
    public int ThumbnailHeight { get; set; } = 360;
    public int ThumbnailQuality { get; set; } = 78;
    public int ThumbnailConcurrency { get; set; } = ThumbnailQueueSettings.DefaultConcurrency;
    public string ResizerServiceUrl { get; set; } = "";
    public string ResizerServiceHost { get; set; } = "";
    public string ResizerServiceApiKey { get; set; } = "";
    public string ResizerServiceCertificateSha256 { get; set; } = "";
    public int ResizerServiceQuality { get; set; } = 78;
    public string ResizerServiceDecodeMode { get; set; } = "full";
    public int ResizerServiceWorkers { get; set; } = 4;
    public int ResizerServiceBackgroundWorkers { get; set; } = 0;
    public int ResizerServiceRetrySeconds { get; set; } = 30;
    public int DefaultItemsPerRow { get; set; } = 8;
    public string FfmpegPath { get; set; } = "C:\\Web\\imagegallery-tools\\ffmpeg.exe";
    public string FfprobePath { get; set; } = "C:\\Web\\imagegallery-tools\\ffprobe.exe";
    public int MaxConcurrentMediaJobs { get; set; } = 16;
    public int MaxConcurrentQuickSyncJobs { get; set; } = 2;
    public int MediaEncoderThreads { get; set; } = 16;
}
