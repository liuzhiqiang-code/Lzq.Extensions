using Lzq.Core.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Lzq.Core;

public class CoreModule : BaseModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMapster();
    }

    public override void OnApplicationInitialization(ModuleInitContext context)
    {
        UseCoreExceptionHandler(context.App);
    }


    private void UseCoreExceptionHandler(IApplicationBuilder app)
    {
        app.UseMasaExceptionHandler(options =>
        {
            var exceptionStatusMap = new Dictionary<Type, int>
            {
                [typeof(UserFriendlyException)] = 200,      // 用户友好异常
                [typeof(MasaArgumentException)] = 400,      // 参数异常
                [typeof(MasaValidatorException)] = 298,     // 验证异常
                [typeof(UnauthorizedAccessException)] = 401,
                [typeof(KeyNotFoundException)] = 404,

            };

            options.ExceptionHandler = context =>
            {
                var loggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("GlobalExceptionHandler");
                var exception = context.Exception;

                // 确定HTTP状态码
                var statusCode = exceptionStatusMap.TryGetValue(exception.GetType(), out var code)
                    ? code
                    : 500;

                // 记录异常日志
                LogException(logger, exception);

                // 创建响应结果
                var apiResult = ApiResult.Fail(exception.Message, statusCode);
                var jsonResponse = apiResult.ToJson();

                // 设置响应
                context.ToResult(jsonResponse, statusCode);
            };
        });
    }

    /// <summary>
    /// 记录异常到日志
    /// </summary>
    private void LogException(ILogger logger, Exception exception)
    {
        var fullMessage = GetFullExceptionMessage(exception);
        logger.LogError(fullMessage, "发生未处理的异常");
    }

    /// <summary>
    /// 获取完整异常信息（包含内层异常）
    /// </summary>
    private string GetFullExceptionMessage(Exception ex)
    {
        var sb = new StringBuilder();
        var currentEx = ex;
        var level = 0;

        while (currentEx != null)
        {
            var indent = new string(' ', level * 2);
            sb.AppendLine($"{indent}【{GetExceptionLevel(level)}】");
            sb.AppendLine($"{indent}  类型: {currentEx.GetType().FullName}");
            sb.AppendLine($"{indent}  消息: {currentEx.Message}");

            if (!string.IsNullOrEmpty(currentEx.StackTrace))
                sb.AppendLine($"{indent}  堆栈: {currentEx.StackTrace}");

            currentEx = currentEx.InnerException;
            level++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取异常级别描述
    /// </summary>
    private string GetExceptionLevel(int level) => level switch
    {
        0 => "外层异常",
        _ => $"内层异常 Lv{level}"
    };
}