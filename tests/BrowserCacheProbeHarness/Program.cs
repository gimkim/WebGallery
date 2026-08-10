var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
var assetRequests = 0;

app.MapGet("/", () => Results.Content("""
<!doctype html><html><head><title>WAIT</title></head><body>WAIT<script>
(async () => {
  const url = '/asset?stamp=123-456';
  await (await fetch(url, { cache: 'default' })).blob();
  const cached = await fetch(url, { cache: 'only-if-cached', mode: 'same-origin' });
  await cached.blob();
  const count = await (await fetch('/count', { cache: 'no-store' })).text();
  const result = cached.ok && count === '1' ? 'PASS' : `FAIL status=${cached.status} requests=${count}`;
  document.title = result;
  document.body.textContent = result;
})().catch(error => {
  document.title = 'FAIL';
  document.body.textContent = `FAIL ${error}`;
});
</script></body></html>
""", "text/html"));

app.MapGet("/asset", () =>
{
    Interlocked.Increment(ref assetRequests);
    return Results.Bytes([1, 2, 3, 4], "application/octet-stream", lastModified: DateTimeOffset.UtcNow);
}).AddEndpointFilter<CacheControlFilter>();

app.MapGet("/count", () => Volatile.Read(ref assetRequests).ToString());
app.Run("http://127.0.0.1:54137");

sealed class CacheControlFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return await next(context);
    }
}
