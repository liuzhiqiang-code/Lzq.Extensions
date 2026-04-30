using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebApiClientCore;
using WebApiClientCore.Attributes;


// 1. 定义拦截器属性
public class ApiLoggingAttribute : ApiFilterAttribute
{
    // WebApiClientCore 会自动处理属性类型的过滤器
    public override async Task OnRequestAsync(ApiRequestContext context)
    {
        var logger = context.HttpContext.ServiceProvider.GetRequiredService<ILogger<ApiLoggingAttribute>>();
        logger.LogInformation("请求地址: {Url}", context.HttpContext.RequestMessage.RequestUri);
        await Task.CompletedTask;
    }

    public override async Task OnResponseAsync(ApiResponseContext context)
    {
        var logger = context.HttpContext.ServiceProvider.GetRequiredService<ILogger<ApiLoggingAttribute>>();
        logger.LogInformation("响应状态: {Code}", context.HttpContext.ResponseMessage?.StatusCode);
        await Task.CompletedTask;
    }
}