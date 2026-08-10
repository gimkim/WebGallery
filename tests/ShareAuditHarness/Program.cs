using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<GalleryDbContext>().UseSqlite(connection).Options;
await using var db = new GalleryDbContext(options);
await db.Database.EnsureCreatedAsync();

var owner = new ApplicationUser { Id = "owner", UserName = "owner", RootFolder = "C:\\gallery" };
var link = new ShareLink
{
    OwnerUserId = owner.Id,
    Owner = owner,
    Token = new string('a', 48),
    RelativePath = "photos"
};
db.Users.Add(owner);
db.ShareLinks.Add(link);
await db.SaveChangesAsync();

var context = new DefaultHttpContext();
context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");
var audit = new ShareAuditService(db, new HttpContextAccessor { HttpContext = context });
await audit.RecordAsync(link, ShareAuditEventTypes.Access, "photos");
await audit.RecordAsync(link, ShareAuditEventTypes.View, "photos/image.jpg");
await audit.RecordAsync(link, ShareAuditEventTypes.DownloadSelection, "photos", 2,
    ["photos/image.jpg", "photos/image-2.jpg"]);

var events = await db.ShareAuditEvents.OrderBy(item => item.Id).ToListAsync();
Assert(events.Count == 3, "Expected three audit rows.");
Assert(events.All(item => item.ClientIp == "203.0.113.42"), "The real direct IP was not stored.");
Assert(events.All(item => item.OccurredAtUnixSeconds > 0), "Every audit event must store an indexed Unix timestamp.");
await db.Database.ExecuteSqlRawAsync("UPDATE ShareAuditEvents SET OccurredAtUnixSeconds = 0 WHERE Id = {0}", events[0].Id);
await db.Database.ExecuteSqlRawAsync("UPDATE ShareAuditEvents SET OccurredAtUnixSeconds = CAST(strftime('%s', OccurredAtUtc) AS INTEGER) WHERE OccurredAtUnixSeconds = 0");
Assert(await db.ShareAuditEvents.AsNoTracking().AllAsync(item => item.OccurredAtUnixSeconds > 0),
    "Existing DateTimeOffset values could not be backfilled to Unix seconds.");
Assert(events.Select(item => item.VisitorHash).Distinct().Count() == 1, "Visitor hash must be stable per link/IP.");
Assert(events[2].ItemCount == 2 && events[2].Details.Contains("image-2.jpg", StringComparison.Ordinal),
    "Selection details were not retained.");

var fromUnix = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
var filteredQuery = db.ShareAuditEvents.AsNoTracking()
    .Where(item => item.OccurredAtUnixSeconds >= fromUnix)
    .Where(item => item.ShareLink!.OwnerUserId == owner.Id)
    .Where(item => EF.Functions.Like(item.ClientIp, "%203.0.113%", "\\"))
    .Where(item => EF.Functions.Like(item.TargetPath, "%image%", "\\")
        || EF.Functions.Like(item.Details, "%image%", "\\")
        || EF.Functions.Like(item.ShareLink!.Owner!.UserName!, "%owner%", "\\"));
var filteredCounts = await filteredQuery.GroupBy(item => item.EventType)
    .Select(group => new { group.Key, Count = group.Count() })
    .ToListAsync();
var filteredEvents = await filteredQuery
    .Include(item => item.ShareLink)!.ThenInclude(item => item!.Owner)
    .Include(item => item.ShareLink)!.ThenInclude(item => item!.Collection)
    .OrderByDescending(item => item.Id)
    .ToListAsync();
Assert(filteredCounts.Sum(item => item.Count) == 3 && filteredEvents.Count == 3,
    "Management log filters or navigation loading did not execute correctly on SQLite.");

var summary = (await audit.GetSummariesAsync([link.Id]))[link.Id];
Assert(summary.AccessCount == 1, "Access summary is incorrect.");
Assert(summary.ViewCount == 1, "View summary is incorrect.");
Assert(summary.DownloadCount == 1, "Download summary is incorrect.");
Assert(summary.UniqueVisitorCount == 1, "Visitor summary is incorrect.");
Assert(summary.LastActivityUtc is not null, "Last activity is missing.");

db.ShareLinks.Remove(link);
await db.SaveChangesAsync();
Assert(!await db.ShareAuditEvents.AnyAsync(), "Audit rows must cascade when the share link is deleted.");

Console.WriteLine("Share audit self-test passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
