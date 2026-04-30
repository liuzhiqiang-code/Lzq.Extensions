using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace Lzq.Extensions.Serilog;


public class HttpRequestEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestEnricher()
    {
        
    }
    public HttpRequestEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null) return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Method", httpContext.Request.Method));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Path", httpContext.Request.Path));
    }
}