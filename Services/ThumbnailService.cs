using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using WebGallery.Models;

namespace WebGallery.Services;

public sealed class ThumbnailService
{
    public const string CacheVersion = "contain-v1";
    private readonly GalleryOptions _options;
    private readonly string _cachePath;
    private readonly ThumbnailWorkQueue _queue;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ThumbnailService(IOptions<GalleryOptions> options, IWebHostEnvironment environment, ThumbnailWorkQueue queue)
    {
        _options = options.Value;
        _queue = queue;
        _cachePath = Path.GetFullPath(_options.CachePath, environment.ContentRootPath);
        Directory.CreateDirectory(_cachePath);
    }

    public async Task<string> GetOrCreateAsync(
        string ownerId,
        string imagePath,
        ThumbnailPriority priority,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(imagePath);
        if (!info.Exists) throw new FileNotFoundException();
        var identity = $"{CacheVersion}|{ownerId}|{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{_options.ThumbnailWidth}x{_options.ThumbnailHeight}|{_options.ThumbnailQuality}";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var folder = Path.Combine(_cachePath, key[..2]);
        var cacheFile = Path.Combine(folder, key + ".webp");
        if (File.Exists(cacheFile)) return cacheFile;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cacheFile)) return cacheFile;
            return await _queue.EnqueueAsync(async jobCancellationToken =>
            {
                if (File.Exists(cacheFile)) return cacheFile;
                Directory.CreateDirectory(folder);
                var temp = cacheFile + $".{Guid.NewGuid():N}.new";
                try
                {
                    using var image = await Image.LoadAsync(imagePath, jobCancellationToken);
                    image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
                    {
                        Size = new Size(_options.ThumbnailWidth, _options.ThumbnailHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3
                    }));
                    await image.SaveAsWebpAsync(temp, new WebpEncoder { Quality = _options.ThumbnailQuality }, jobCancellationToken);
                    File.Move(temp, cacheFile, true);
                    return cacheFile;
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }, priority, cancellationToken);
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
                ((ICollection<KeyValuePair<string, SemaphoreSlim>>)_locks).Remove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
        }
    }
}
