using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using WebGallery.Models;

namespace WebGallery.Services;

public sealed class ThumbnailService
{
    public const string CacheVersion = "contain-v1";
    private readonly GalleryOptions _options;
    private readonly string _cachePath;
    public string CachePath => _cachePath;
    public string Signature => $"{_settings.CacheVersion}|{_options.ThumbnailWidth}x{_options.ThumbnailHeight}|{_options.ThumbnailQuality}";
    public string MediumSignature => $"{_settings.CacheVersion}|1500x1500|{_options.ThumbnailQuality}";
    private readonly ThumbnailWorkQueue _queue;
    private readonly ThumbnailQueueSettings _settings;
    private readonly GalleryIndexService? _index;
    private readonly RemoteResizer? _remote;
    // Bounded lock stripes are never removed while another caller may still hold a reference.
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 1024).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public ThumbnailService(IOptions<GalleryOptions> options, IWebHostEnvironment environment, ThumbnailWorkQueue queue, ThumbnailQueueSettings settings, GalleryIndexService? index = null, RemoteResizer? remote = null)
    {
        _options = options.Value;
        _queue = queue;
        _settings = settings;
        _index = index;
        _remote = remote;
        settings.DecodeProvider = () => remote?.Enabled == true ? remote.DecodeMode == "jpeg-idct" : settings.ReducedJpeg;
        settings.BackgroundWorkersProvider = () => remote?.IsAvailable == true ? remote.BackgroundWorkers : settings.BackgroundWorkers;
        if (remote is not null) remote.DecodeChanged += settings.NotifyChanged;
        _cachePath = Path.GetFullPath(_options.CachePath, environment.ContentRootPath);
        Directory.CreateDirectory(_cachePath);
    }

    public async Task<string> GetOrCreateAsync(
        string ownerId,
        string imagePath,
        ThumbnailPriority priority,
        CancellationToken cancellationToken,
        string? version = null, bool medium = false)
    {
        var backgroundMedium = medium && priority == ThumbnailPriority.Background;
        if (backgroundMedium && _index?.HasRunnableSmall(Signature) == true) throw new SmallThumbnailsPendingException();
        var width = medium ? 1500 : _options.ThumbnailWidth;
        var height = medium ? 1500 : _options.ThumbnailHeight;
        var info = new FileInfo(imagePath);
        if (!info.Exists) throw new FileNotFoundException();
        // One rendition identity per decoder, independent of execution host and service availability.
        var cacheVersion = version is "contain-v1" or "contain-idct-v1" ? version : _settings.CacheVersion;
        var remoteFast = cacheVersion == "contain-idct-v1";
        var physicalIdentity = OperatingSystem.IsWindows() ? info.FullName.ToUpperInvariant() : info.FullName;
        var identity = $"shared-v1|{cacheVersion}|{physicalIdentity}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{width}x{height}|{_options.ThumbnailQuality}";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var folder = Path.Combine(_cachePath, key[..2]);
        var cacheFile = Path.Combine(folder, key + ".webp");
        void RecordReady() {
            var signature = $"{cacheVersion}|{width}x{height}|{_options.ThumbnailQuality}";
            if (medium) _index?.TryRecordMediumThumbnail(ownerId, info.FullName, info.Length, info.LastWriteTimeUtc.Ticks, signature);
            else _index?.TryRecordThumbnail(ownerId, info.FullName, info.Length, info.LastWriteTimeUtc.Ticks, signature);
        }
        // Serving an existing WebP must not wait for SQLite's derived-index write lock.
        // The indexed background feed records readiness after its own cache-hit request.
        if (File.Exists(cacheFile)) return cacheFile;
        using var localPause = priority != ThumbnailPriority.Background ? _queue.SuspendBackground() : null;
        using var remotePause = priority != ThumbnailPriority.Background ? _remote?.SuspendBackground() : null;
        // A direct medium request also ensures its small rendition exists first.
        if (medium && priority != ThumbnailPriority.Background)
            await GetOrCreateAsync(ownerId, imagePath, priority, cancellationToken, cacheVersion);

        if (_remote?.Enabled == true) {
            Directory.CreateDirectory(folder);
            var remoteTemp = cacheFile + $".{Guid.NewGuid():N}.new";
            try {
                var ready = await _remote.TryCreateAsync(imagePath, remoteTemp, width, height,
                    remoteFast, priority,
                    _locks[Convert.ToInt32(key[..3],16) % _locks.Length], () => File.Exists(cacheFile), async ct => {
                        var header = await Image.IdentifyAsync(remoteTemp, ct);
                        if (header.Width > width || header.Height > height) return false;
                        var current = new FileInfo(imagePath);
                        if (!current.Exists || current.Length != info.Length || current.LastWriteTimeUtc.Ticks != info.LastWriteTimeUtc.Ticks) return false;
                        File.Move(remoteTemp,cacheFile,true); return true;
                    }, cancellationToken, _options.ThumbnailQuality);
                if (ready) { RecordReady(); return cacheFile; }
                if (_remote.IsAvailable) throw new RemoteThumbnailRejectedException("Remote result could not be published; source or output changed. Local decoding was not attempted.");
            } finally { if (File.Exists(remoteTemp)) File.Delete(remoteTemp); }
        }

        if (priority == ThumbnailPriority.Background && _settings.BackgroundWorkers == 0)
            throw new InvalidOperationException("Local background generation is disabled; retry when the remote service is available.");
        string result;
        try { result = await _queue.EnqueueAsync(async jobCancellationToken =>
        {
            if (_remote?.IsAvailable == true) throw new RemoteThumbnailRecoveredException();
            if (backgroundMedium && _index?.HasRunnableSmall(Signature) == true) throw new SmallThumbnailsPendingException();
            var gate = _locks[Convert.ToInt32(key[..3], 16) % _locks.Length];
            await gate.WaitAsync(jobCancellationToken);
            try
            {
                if (File.Exists(cacheFile)) return cacheFile;
                if (_remote?.IsAvailable == true) throw new RemoteThumbnailRecoveredException();
                Directory.CreateDirectory(folder);
                var temp = cacheFile + $".{Guid.NewGuid():N}.new";
                try
                {
                    using var image = await DecodeAsync(imagePath, cacheVersion.StartsWith("contain-idct-v1", StringComparison.Ordinal), width, height, jobCancellationToken, _options.ExifToolPath, _options.FfmpegPath);
                    image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
                    {
                        Size = new Size(width, height),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3
                    }));
                    await image.SaveAsWebpAsync(temp, new WebpEncoder { Quality = _options.ThumbnailQuality }, jobCancellationToken);
                    var current = new FileInfo(imagePath);
                    if (!current.Exists || current.Length != info.Length || current.LastWriteTimeUtc.Ticks != info.LastWriteTimeUtc.Ticks)
                        throw new IOException("Image changed during thumbnail generation; retry with its new fingerprint.");
                    File.Move(temp, cacheFile, true);
                    return cacheFile;
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
            finally { gate.Release(); }
        }, priority, cancellationToken, medium); }
        catch (RemoteThumbnailRecoveredException) {
            return await GetOrCreateAsync(ownerId, imagePath, priority, cancellationToken, version, medium);
        }
        RecordReady();
        return result;
    }

    public static async Task<Image> DecodeAsync(string path, bool reduced, int width, int height, CancellationToken cancellationToken, string exifToolPath = "exiftool", string ffmpegPath = "ffmpeg")
    {
        await using var stream = await VideoThumbnail.OpenAsync(path,ffmpegPath,exifToolPath,cancellationToken);
        if (!reduced) return await Image.LoadAsync(stream, cancellationToken);
        var header = new byte[2];
        var count = await stream.ReadAsync(header, cancellationToken);
        stream.Position = 0;
        if (count != 2 || header[0] != 0xff || header[1] != 0xd8)
            return await Image.LoadAsync(stream, cancellationToken);
        return await JpegDecoder.Instance.DecodeAsync(new JpegDecoderOptions
        {
            GeneralOptions = new DecoderOptions { TargetSize = new Size(Math.Max(width, height), Math.Max(width, height)) },
            ResizeMode = JpegDecoderResizeMode.IdctOnly
        }, stream, cancellationToken);
    }
}

public sealed class MediumThumbnailUnavailableException : Exception;
public sealed class SmallThumbnailsPendingException : Exception;
