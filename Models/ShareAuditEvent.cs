namespace WebGallery.Models;

public static class ShareAuditEventTypes
{
    public const string Access = "Access";
    public const string View = "View";
    public const string DownloadFile = "DownloadFile";
    public const string DownloadFolder = "DownloadFolder";
    public const string DownloadCollection = "DownloadCollection";
    public const string DownloadSelection = "DownloadSelection";

    public static bool IsDownload(string eventType) => eventType.StartsWith("Download", StringComparison.Ordinal);
}

public sealed class ShareAuditEvent
{
    public long Id { get; set; }
    public int ShareLinkId { get; set; }
    public ShareLink? ShareLink { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long OccurredAtUnixSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public required string EventType { get; set; }
    public string TargetPath { get; set; } = "";
    public string Details { get; set; } = "";
    public int ItemCount { get; set; } = 1;
    public required string ClientIp { get; set; }
    public required string VisitorHash { get; set; }
}
