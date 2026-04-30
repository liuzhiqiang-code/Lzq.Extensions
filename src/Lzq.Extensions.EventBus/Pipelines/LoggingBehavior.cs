using MediatR;
using Microsoft.Extensions.Logging;

namespace Lzq.Extensions.EventBus.Pipelines;

public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var typeName = request.GetType().Name;
        logger.LogInformation("[EventBus] 开始执行 {CommandName} {@CommandContent}", typeName, request);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        logger.LogInformation("[EventBus] 执行结束 {CommandName}, 耗时: {Elapsed}ms", typeName, stopwatch.ElapsedMilliseconds);
        return response;
    }
}