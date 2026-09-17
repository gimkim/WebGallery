using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using WebGallery.Data;
using WebGallery.Models;

namespace WebGallery.Services;

// Bounded parallel metadata readers; no work on the navigation request path.
public sealed class DateTakenIndexer(IServiceScopeFactory scopes, IOptions<GalleryOptions> options,
    ILogger<DateTakenIndexer> logger) : BackgroundService
{
    private int workers = 4;
    public int Workers => Volatile.Read(ref workers);
    public void SetWorkers(int value) => Volatile.Write(ref workers, Math.Clamp(value, 1, 16));
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Root, string Parent), long> requested = new();
    public void RequestFolder(int rootId, string parentKey)
    {
        if (requested.Count < 256 || requested.ContainsKey((rootId, parentKey)))
            requested[(rootId, parentKey)] = Environment.TickCount64;
    }
    public static DateTime? Parse(string? value) => DateTime.TryParseExact(value?.Trim('\0', ' ', '\r', '\n'),
        "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static async Task<DateTime?> ReadAsync(string path, string exifTool, CancellationToken token)
    {
        if (!RawPreview.IsRaw(path)) {
            await using var stream = File.OpenRead(path);
            var signature = new byte[2];
            var length = await stream.ReadAsync(signature, token);
            stream.Position = 0;
            if (length == 2 && signature[0] == 0xff && signature[1] == 0xd8)
                return await JpegDateTaken.ReadAsync(stream, token);
            var info = await Image.IdentifyAsync(stream, token);
            return info?.Metadata.ExifProfile?.TryGetValue(ExifTag.DateTimeOriginal, out var original) == true
                ? Parse(original.Value) : null;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo(exifTool) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-config", "", "-s3", "-EXIF:DateTimeOriginal", "--", Path.GetFullPath(path) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start metadata reader.");
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        // Fixed tag request; bound captured output even for malformed metadata.
        var buffer = new char[256];
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        var read = await process.StandardOutput.ReadBlockAsync(buffer.AsMemory(), timeout.Token);
        await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        await stderr;
        if (process.ExitCode != 0) throw new IOException("RAW metadata reader failed.");
        return Parse(new string(buffer, 0, read));
    }

    private sealed record Result(GalleryIndexEntry Entry, long? Ticks, long Next);
    private static string Identity(GalleryIndexEntry entry) => $"{entry.RootId}|{entry.PathKey}";

    private async Task<List<GalleryIndexEntry>> FetchAsync(HashSet<string> held, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pending = db.GalleryIndexEntries.AsNoTracking().Where(x => x.IsImage && !x.IsDirectory && x.DateTakenScanAfter <= now);
        var limit = Math.Min(64, 128 - held.Count);
        async Task<List<GalleryIndexEntry>> Page(IQueryable<GalleryIndexEntry> query) =>
            (await query.OrderBy(x => x.DateTakenScanAfter).ThenBy(x => x.RootId).ThenBy(x => x.PathKey)
                .Take(limit + held.Count).ToListAsync(token)).Where(x => !held.Contains(Identity(x))).Take(limit).ToList();
        foreach (var request in requested.OrderByDescending(x => x.Value).Take(8)) {
            var page = await Page(pending.Where(x => x.RootId == request.Key.Root && x.ParentKey == request.Key.Parent));
            if (page.Count > 0) return page;
            requested.TryRemove(request.Key, out _);
        }
        return await Page(pending);
    }

    private async Task<Result> ReadEntryAsync(GalleryIndexEntry entry, CancellationToken token)
    {
        long? ticks = null; long next = long.MaxValue;
        try {
            // Every reader owns its own scope/DbContext. Never share EF contexts across tasks.
            using var scope = scopes.CreateScope();
            var files = new FileSystemService(scope.ServiceProvider.GetRequiredService<GalleryDbContext>());
            var path = files.ResolvePath(new ApplicationUser { Id = entry.OwnerId },
                FileSystemService.RootMarker(entry.RootId) + "/" + entry.RelativePath);
            if (FileSystemService.IsIgnoredFileSystemEntry(path)) throw new IOException("Source unavailable.");
            var before = new FileInfo(path);
            if (before.Length != entry.Size || before.LastWriteTimeUtc.Ticks != entry.ModifiedTicks) throw new IOException("Source changed.");
            ticks = (await ReadAsync(path, options.Value.ExifToolPath, token))?.Ticks;
            var after = new FileInfo(path);
            if (after.Length != entry.Size || after.LastWriteTimeUtc.Ticks != entry.ModifiedTicks) throw new IOException("Source changed.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnknownImageFormatException) { }
        catch (InvalidImageContentException) { }
        catch (Exception) { ticks = null; next = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; }
        return new Result(entry, ticks, next);
    }

    private async Task<List<Result>> SaveAsync(List<Result> results, CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        foreach (var result in results) {
            var e = result.Entry;
            await db.GalleryIndexEntries.Where(x => x.RootId == e.RootId && x.PathKey == e.PathKey
                && x.Size == e.Size && x.ModifiedTicks == e.ModifiedTicks && x.OwnerId == e.OwnerId && x.PhysicalKey == e.PhysicalKey)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DateTakenTicks, result.Ticks)
                    .SetProperty(x => x.DateTakenScanAfter, result.Next), token);
        }
        await transaction.CommitAsync(token);
        return results;
    }

    private async Task RunPipelineAsync(CancellationToken token)
    {
        var held = new HashSet<string>();
        var ready = new Queue<GalleryIndexEntry>();
        var active = new List<Task<Result>>();
        var completed = new List<Result>();
        Task<List<Result>>? saving = null;
        long nextFetch = 0, lastSave = Environment.TickCount64;
        try {
            while (!token.IsCancellationRequested) {
                if (saving?.IsCompleted == true) {
                    foreach (var result in await saving) held.Remove(Identity(result.Entry));
                    saving = null;
                }
                foreach (var task in active.Where(x => x.IsCompleted).ToArray()) {
                    completed.Add(await task); active.Remove(task);
                }
                if (saving is null && completed.Count > 0 &&
                    (completed.Count >= 32 || Environment.TickCount64 - lastSave >= 500 || active.Count == 0 && ready.Count == 0)) {
                    var batch = completed.Take(32).ToList(); completed.RemoveRange(0, batch.Count);
                    saving = Task.Run(() => SaveAsync(batch, token), token); lastSave = Environment.TickCount64;
                }
                // A completed read frees its slot immediately; SQLite commits run separately.
                while (active.Count < Workers && ready.TryDequeue(out var entry))
                    active.Add(Task.Run(() => ReadEntryAsync(entry, token), token));
                if (ready.Count <= 32 && held.Count < 128 && Environment.TickCount64 >= nextFetch) {
                    var page = await FetchAsync(held, token);
                    foreach (var entry in page) if (held.Add(Identity(entry))) ready.Enqueue(entry);
                    nextFetch = page.Count == 0 ? Environment.TickCount64 + 1000 : 0;
                    while (active.Count < Workers && ready.TryDequeue(out var entry))
                        active.Add(Task.Run(() => ReadEntryAsync(entry, token), token));
                }
                var waits = active.Cast<Task>().ToList();
                if (saving is not null) waits.Add(saving);
                waits.Add(Task.Delay(100, token)); // idle/settings wakeup only; not a per-file delay
                await Task.WhenAny(waits);
            }
        } finally {
            // Observe all tasks before retry/shutdown; in-memory unsaved rows remain eligible.
            try { await Task.WhenAll(active); } catch { }
            if (saving is not null) { try { await saving; } catch { } }
        }
        token.ThrowIfCancellationRequested();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested) {
            try { await RunPipelineAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Date taken indexing will retry"); await Task.Delay(5000, stoppingToken); }
        }
    }
}
