using Microsoft.AspNetCore.Http;
using System.Reflection;
using WebApiClientCore;

namespace Lzq.Extensions.WebApiClientCore.Aop;

public class HttpContextHeaderFilter : IApiFilter
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextHeaderFilter(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public async Task OnRequestAsync(ApiRequestContext context)
    {
        var httpContext = _accessor.HttpContext;
        if (httpContext == null) return;

        // 检查接口是否标注了 NeedAuthToken 特性
        var needAuth = context.ActionDescriptor.InterfaceType.GetCustomAttribute<NeedAuthTokenAttribute>() != null;

        if (needAuth)
        {
            // 1. 注入 TraceId (兼容 Masa 或标准 OpenTelemetry)
            var traceId = httpContext.TraceIdentifier;
            if (!string.IsNullOrEmpty(traceId))
            {
                context.HttpContext.RequestMessage.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
            }

            // 2. 自动透传 Authorization Token
            var authHeader = httpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                context.HttpContext.RequestMessage.Headers.TryAddWithoutValidation("Authorization", authHeader);
            }

            // 3. 注入自定义业务 Header (如租户 ID)
            var tenantId = httpContext.Request.Headers["__tenant__"].ToString();
            if (!string.IsNullOrEmpty(tenantId))
            {
                context.HttpContext.RequestMessage.Headers.TryAddWithoutValidation("x-tenant-id", tenantId);
            }
        }
        await Task.CompletedTask;
    }

    public Task OnResponseAsync(ApiResponseContext context) => Task.CompletedTask;
}