using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WebGallery.Models;

namespace WebGallery.Services;
public sealed class RemoteThumbnailBusyException : Exception;
public sealed class RemoteThumbnailRejectedException(string message) : Exception(message);
public sealed class RemoteThumbnailRecoveredException : Exception;

public sealed class RemoteResizer : IHostedService, IDisposable
{
    private readonly GalleryOptions options;
    private readonly HttpClient client;
    private readonly ThumbnailWorkQueue queue;
    private readonly ThumbnailQueueSettings limits;
    private int quality;
    private int reducedJpeg;
    public event Action? DecodeChanged;
    public string DecodeMode => Volatile.Read(ref reducedJpeg) == 1 ? "jpeg-idct" : "full";
    private readonly Dictionary<string,bool> decodeBySuffix = new(StringComparer.Ordinal);
    public bool FastForVersion(string? version) {
        var offset = version?.IndexOf("-remote-",StringComparison.Ordinal) ?? -1;
        return offset >= 0 && decodeBySuffix.TryGetValue(version![offset..],out var value) ? value : DecodeMode == "jpeg-idct";
    }
    public int BackgroundWorkers => limits.BackgroundWorkers;
    public bool IsAvailable => Enabled && Volatile.Read(ref online) == 1;
    public void UpdateDecodeSettings(string mode, int workers, int? backgroundWorkers = null) {
        var changed = Interlocked.Exchange(ref reducedJpeg,mode == "jpeg-idct" ? 1 : 0) != (mode == "jpeg-idct" ? 1 : 0);
        changed |= workers != Workers || (backgroundWorkers.HasValue && backgroundWorkers.Value != BackgroundWorkers);
        limits.SetBackgroundWorkers(Math.Clamp(backgroundWorkers ?? BackgroundWorkers,0,Math.Clamp(workers,1,16)));
        limits.Update(workers);
        if (changed) DecodeChanged?.Invoke();
    }
    private readonly Dictionary<string,int> qualityBySuffix = new(StringComparer.Ordinal);
    public int Quality => Volatile.Read(ref quality);
    public int QualityForVersion(string? version) {
        var offset = version?.IndexOf("-remote-",StringComparison.Ordinal) ?? -1;
        return offset >= 0 && qualityBySuffix.TryGetValue(version![offset..],out var value) ? value : Quality;
    }
    public int Workers => limits.MaxConcurrency;
    public void UpdateSettings(int newQuality, int workers) {
        Volatile.Write(ref quality, Math.Clamp(newQuality,1,100));
        limits.Update(workers);
    }
    private int online;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim wake = new(0,1);
    private Task? monitor;
    private long checkedTicks;
    public string? LastFailure { get; private set; }
    public DateTimeOffset? LastCheckedUtc => Interlocked.Read(ref checkedTicks) is var ticks && ticks > 0 ? new DateTimeOffset(ticks,TimeSpan.Zero) : null;
    public bool Enabled => Uri.TryCreate(options.ResizerServiceUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https";
    public string Status => !Enabled ? "Disabled" : Volatile.Read(ref online) == 1 ? "Connected · remote service active" : "Unavailable or checking · using local";
    public void RetryNow() { try { wake.Release(); } catch (SemaphoreFullException) {} }
    public string CacheSuffix => GetCacheSuffix(Quality);
    public string GetCacheSuffix(int value, bool? fast = null) => Enabled ? "-remote-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(options.ResizerServiceUrl + "|" + value)))[..12].ToLowerInvariant() + ((fast ?? DecodeMode == "jpeg-idct") ? "-idct" : "-full") : "";
    public RemoteResizer(IOptions<GalleryOptions> settings, HttpMessageHandler? transport = null)
    {
        options = settings.Value;
        reducedJpeg = options.ResizerServiceDecodeMode == "jpeg-idct" ? 1 : 0;
        if (Enabled) for (var value = 1; value <= 100; value++) foreach (var fast in new[] {false,true}) {
            var suffix = GetCacheSuffix(value,fast); qualityBySuffix[suffix] = value; decodeBySuffix[suffix] = fast;
        }
        quality = Math.Clamp(options.ResizerServiceQuality,1,100);
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2), AllowAutoRedirect = false, MaxConnectionsPerServer = 16 };
        if (!string.IsNullOrWhiteSpace(options.ResizerServiceCertificateSha256))
            handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, errors) => cert is not null
                && DateTime.Now >= ((System.Security.Cryptography.X509Certificates.X509Certificate2)cert).NotBefore && DateTime.Now <= ((System.Security.Cryptography.X509Certificates.X509Certificate2)cert).NotAfter
                && string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), options.ResizerServiceCertificateSha256, StringComparison.OrdinalIgnoreCase);
        if (transport is not null) handler.Dispose();
        client = new HttpClient(transport ?? handler) { Timeout = TimeSpan.FromSeconds(45) };
        limits = new ThumbnailQueueSettings(Math.Clamp(options.ResizerServiceWorkers,1,16)); limits.SetBackgroundWorkers(Math.Clamp(options.ResizerServiceBackgroundWorkers,0,limits.MaxConcurrency));
        queue = new ThumbnailWorkQueue(limits);
    }
    public async Task<bool> TryCreateAsync(string source, string output, int width, int height, bool fast, ThumbnailPriority priority, SemaphoreSlim gate, Func<bool> alreadyCached, Func<CancellationToken,Task<bool>> publish, CancellationToken token, int? requestedQuality = null)
    {
        if (!Enabled || Volatile.Read(ref online) == 0) return false;
        var jobQuality = Math.Clamp(requestedQuality ?? Quality,1,100);
        while (true) { try {
            return await queue.EnqueueAsync(async ct => {
                if (Volatile.Read(ref online) == 0) return false;
                try {
                    await gate.WaitAsync(ct);
                    try {
                        if (alreadyCached()) return true;
                        if (Volatile.Read(ref online) == 0) return false;
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(45));
                        var networkToken = timeout.Token;
                        using var file = await VideoThumbnail.OpenAsync(source,options.FfmpegPath,options.ExifToolPath,ct);
                        using var request = new HttpRequestMessage(HttpMethod.Post, options.ResizerServiceUrl.TrimEnd('/') + $"/resize?width={width}&height={height}&mode={(fast ? "fast" : "full")}&quality={jobQuality}");
                        request.Headers.Add("X-Thumb-Key",options.ResizerServiceApiKey);
                        if (!string.IsNullOrWhiteSpace(options.ResizerServiceHost)) request.Headers.Host = options.ResizerServiceHost;
                        request.Content = new StreamContent(file);
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, networkToken);
                        if (!response.IsSuccessStatusCode) {
                            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RemoteThumbnailBusyException();
                            if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge)
                                throw new RemoteThumbnailRejectedException($"Thumbnail service rejected this image (HTTP {(int)response.StatusCode}); local decoding was not attempted.");
                            Fail();
                            return false;
                        }
                        if (response.Content.Headers.ContentType?.MediaType != "image/webp") { Fail(); return false; }
                        await using var input = await response.Content.ReadAsStreamAsync(networkToken);
                        await using (var target = File.Create(output)) {
                            var buffer = new byte[65536]; long total = 0; int read;
                            while ((read = await input.ReadAsync(buffer,networkToken)) > 0) { total += read; if (total > 16 * 1024 * 1024) throw new IOException("Oversized thumbnail response"); await target.WriteAsync(buffer.AsMemory(0,read),networkToken); }
                        }
                        return await publish(ct);
                    } finally { gate.Release(); }
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                  catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or SixLabors.ImageSharp.InvalidImageContentException or SixLabors.ImageSharp.UnknownImageFormatException) { Fail(); return false; }
            }, priority, token, medium: width == 1500 && height == 1500);
        } catch (Exception ex) when (ex is ThumbnailQueueFullException or RemoteThumbnailBusyException) {
            // Backpressure is not an outage. Release the remote slot and retry there.
            await Task.Delay(1000, token);
            if (!IsAvailable) return false;
        } }
    }
    public IDisposable SuspendBackground() => queue.SuspendBackground();
    private void SetOnline(bool available) { if (Interlocked.Exchange(ref online,available ? 1 : 0) != (available ? 1 : 0)) DecodeChanged?.Invoke(); }
    private void Fail() => SetOnline(false);
    private async Task PollAsync(CancellationToken token) {
        try {
            while (!token.IsCancellationRequested) {
                if (Enabled) {
                    try {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                        using var request = new HttpRequestMessage(HttpMethod.Get,options.ResizerServiceUrl.TrimEnd('/') + "/health");
                        request.Headers.Add("X-Thumb-Key",options.ResizerServiceApiKey);
                        if (!string.IsNullOrWhiteSpace(options.ResizerServiceHost)) request.Headers.Host = options.ResizerServiceHost;
                        using var response = await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                        await response.Content.LoadIntoBufferAsync(4096,timeout.Token);
                        using var health = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                        SetOnline(response.IsSuccessStatusCode && health.RootElement.TryGetProperty("status",out var status) && status.GetString() == "ok");
                        LastFailure = Volatile.Read(ref online) == 1 ? null : $"Health check returned HTTP {(int)response.StatusCode}.";
                    } catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or IOException) { LastFailure = ex.GetBaseException().Message; Fail(); }
                    Interlocked.Exchange(ref checkedTicks,DateTimeOffset.UtcNow.Ticks);
                }
                await wake.WaitAsync(TimeSpan.FromSeconds(Math.Clamp(options.ResizerServiceRetrySeconds,5,600)),token);
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {}
    }
    public async Task StartAsync(CancellationToken token) { await queue.StartAsync(token); monitor = PollAsync(stopping.Token); }
    public async Task StopAsync(CancellationToken token) { stopping.Cancel(); if (monitor is not null) await monitor.WaitAsync(token); await queue.StopAsync(token); }
    public void Dispose() { stopping.Cancel(); client.Dispose(); queue.Dispose(); stopping.Dispose(); wake.Dispose(); }
}
