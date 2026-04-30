using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Lzq.Extensions.Serilog;


public class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpLoggingMiddleware> _logger;

    public HttpLoggingMiddleware(RequestDelegate next, ILogger<HttpLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
            sw.Stop();

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HTTP {Method} {Path} failed after {ElapsedMs}ms",
                context.Request.Method,
                context.Request.Path,
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}