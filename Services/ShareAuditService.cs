using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebGallery.Data;
using WebGallery.Models;

namespace WebGallery.Services;

public sealed class ShareAuditService(GalleryDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task RecordAsync(
        ShareLink link,
        string eventType,
        string? targetPath,
        int itemCount = 1,
        IEnumerable<string>? detailPaths = null,
        CancellationToken cancellationToken = default)
    {
        var clientAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var occurredAtUtc = DateTimeOffset.UtcNow;
        db.ShareAuditEvents.Add(new ShareAuditEvent
        {
            ShareLinkId = link.Id,
            OccurredAtUtc = occurredAtUtc,
            OccurredAtUnixSeconds = occurredAtUtc.ToUnixTimeSeconds(),
            EventType = eventType,
            TargetPath = Truncate((targetPath ?? "").Replace('\\', '/'), 2048),
            Details = detailPaths is null ? "" : SerializeDetails(detailPaths),
            ItemCount = Math.Max(0, itemCount),
            ClientIp = Truncate(clientAddress, 64),
            VisitorHash = CreateVisitorHash(link.Token, clientAddress)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, ShareAuditSummary>> GetSummariesAsync(
        IEnumerable<int> shareLinkIds,
        CancellationToken cancellationToken = default)
    {
        var ids = shareLinkIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, ShareAuditSummary>();

        var eventCounts = await db.ShareAuditEvents
            .Where(item => ids.Contains(item.ShareLinkId))
            .GroupBy(item => new { item.ShareLinkId, item.EventType })
            .Select(group => new { group.Key.ShareLinkId, group.Key.EventType, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var visitorCounts = await db.ShareAuditEvents
            .Where(item => ids.Contains(item.ShareLinkId))
            .GroupBy(item => item.ShareLinkId)
            .Select(group => new { ShareLinkId = group.Key, Count = group.Select(item => item.VisitorHash).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var latestIds = await db.ShareAuditEvents
            .Where(item => ids.Contains(item.ShareLinkId))
            .GroupBy(item => item.ShareLinkId)
            .Select(group => new { ShareLinkId = group.Key, EventId = group.Max(item => item.Id) })
            .ToListAsync(cancellationToken);
        var latestEventIds = latestIds.Select(item => item.EventId).ToArray();
        var latestEvents = await db.ShareAuditEvents
            .Where(item => latestEventIds.Contains(item.Id))
            .Select(item => new { item.ShareLinkId, item.OccurredAtUtc })
            .ToListAsync(cancellationToken);

        return ids.ToDictionary(id => id, id =>
        {
            var counts = eventCounts.Where(item => item.ShareLinkId == id).ToList();
            return new ShareAuditSummary(
                counts.Where(item => item.EventType == ShareAuditEventTypes.Access).Sum(item => item.Count),
                counts.Where(item => item.EventType == ShareAuditEventTypes.View).Sum(item => item.Count),
                counts.Where(item => ShareAuditEventTypes.IsDownload(item.EventType)).Sum(item => item.Count),
                visitorCounts.FirstOrDefault(item => item.ShareLinkId == id)?.Count ?? 0,
                latestEvents.FirstOrDefault(item => item.ShareLinkId == id)?.OccurredAtUtc);
        });
    }

    private static string CreateVisitorHash(string token, string clientAddress)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{token}|{clientAddress}"));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string SerializeDetails(IEnumerable<string> paths)
    {
        var selected = new List<string>();
        foreach (var path in paths.Take(100))
        {
            selected.Add(Truncate(path.Replace('\\', '/'), 1024));
            var candidate = JsonSerializer.Serialize(selected);
            if (candidate.Length <= 4096) continue;
            selected.RemoveAt(selected.Count - 1);
            break;
        }
        return JsonSerializer.Serialize(selected);
    }
}

public sealed record ShareAuditSummary(
    int AccessCount,
    int ViewCount,
    int DownloadCount,
    int UniqueVisitorCount,
    DateTimeOffset? LastActivityUtc);
