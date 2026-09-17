using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

var builder = WebApplication.CreateBuilder(args);
const long maxUpload = 128L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUpload);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxUpload);
var app = builder.Build();
var key = builder.Configuration["ThumbService:ApiKey"] ?? "";
if (key.Length < 32) throw new InvalidOperationException("Configure ThumbService:ApiKey with at least 32 random characters.");
var workers = Math.Clamp(builder.Configuration.GetValue("ThumbService:Workers", 16), 1, 16);
var quality = Math.Clamp(builder.Configuration.GetValue("ThumbService:Quality", 78), 1, 100);
using var slots = new SemaphoreSlim(workers, workers);
var expected = SHA256.HashData(Encoding.UTF8.GetBytes(key));
app.Use(async (context, next) => {
    var presented = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Thumb-Key"].ToString()));
    if (!CryptographicOperations.FixedTimeEquals(expected, presented)) { context.Response.StatusCode = 401; return; }
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { status = "ok", workers, quality, modes = new[] { "full", "fast" } }));
app.MapPost("/resize", async (HttpContext context) => {
    var token = context.RequestAborted;
    if (context.Request.ContentLength is null or <= 0 or > maxUpload) return Results.StatusCode(413);
    if (!int.TryParse(context.Request.Query["width"], out var width) || !int.TryParse(context.Request.Query["height"], out var height)
        || width is < 1 or > 2048 || height is < 1 or > 2048) return Results.BadRequest();
    var mode = context.Request.Query["mode"].ToString();
    if (mode is not ("full" or "fast")) return Results.BadRequest();
    var requestedQuality = int.TryParse(context.Request.Query["quality"], out var q) ? q : quality;
    if (requestedQuality is < 1 or > 100) return Results.BadRequest();
    // Reject excess work before buffering uploads; Gallery retries elsewhere under its own queue.
    if (!await slots.WaitAsync(0, token)) { context.Response.Headers.RetryAfter = "1"; return Results.StatusCode(429); }
    try {
        using var source = new MemoryStream();
        var chunk = new byte[65536]; int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, token)) > 0) {
            if (source.Length + read > maxUpload) return Results.StatusCode(413);
            await source.WriteAsync(chunk.AsMemory(0, read), token);
        }
        source.Position = 0;
        var metadata = await Image.IdentifyAsync(source, token);
        if ((long)metadata.Width * metadata.Height > 120_000_000) return Results.StatusCode(413);
        source.Position = 0;
        var jpeg = source.Length > 2 && source.GetBuffer()[0] == 0xff && source.GetBuffer()[1] == 0xd8;
        using var image = mode == "fast" && jpeg
            ? await JpegDecoder.Instance.DecodeAsync(new JpegDecoderOptions { GeneralOptions = new DecoderOptions { TargetSize = new Size(Math.Max(width,height), Math.Max(width,height)) }, ResizeMode = JpegDecoderResizeMode.IdctOnly }, source, token)
            : await Image.LoadAsync(source, token);
        image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Size = new Size(width,height), Mode = ResizeMode.Max, Sampler = KnownResamplers.Lanczos3 }));
        using var output = new MemoryStream();
        await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = requestedQuality }, token);
        return Results.File(output.ToArray(), "image/webp");
    } catch (UnknownImageFormatException) { return Results.UnprocessableEntity(); }
      catch (InvalidImageContentException) { return Results.UnprocessableEntity(); }
    finally { slots.Release(); }
});
app.Run();
