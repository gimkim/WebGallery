using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebGallery.Services;

// Content negotiation keeps every ordinary href directly loadable in a new tab.
public sealed class PageApiFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (context.Result is ViewResult) context.HttpContext.Response.Headers.Append("Vary", "X-Gallery-Page");
        if (context.Result is not ViewResult || request.Headers["X-Gallery-Page"] != "1") { await next(); return; }
        var response = context.HttpContext.Response;
        var original = response.Body;
        await using var buffer = new MemoryStream();
        response.Body = buffer;
        ResultExecutedContext executed;
        try { executed = await next(); }
        finally { response.Body = original; }
        if (executed.Exception is not null && !executed.ExceptionHandled) return;
        var html = Encoding.UTF8.GetString(buffer.ToArray());
        response.Headers.CacheControl = "no-store";
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { html, url = request.PathBase + request.Path + request.QueryString });
        response.ContentType = "application/vnd.webgallery.page+json; charset=utf-8";
        response.ContentLength = bytes.Length;
        await response.Body.WriteAsync(bytes, context.HttpContext.RequestAborted);
    }
}
