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
Assert(events.Select(item => item.VisitorHash).Distinct().Count() == 1, "Visitor hash must be stable per link/IP.");
Assert(events[2].ItemCount == 2 && events[2].Details.Contains("image-2.jpg", StringComparison.Ordinal),
    "Selection details were not retained.");

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
