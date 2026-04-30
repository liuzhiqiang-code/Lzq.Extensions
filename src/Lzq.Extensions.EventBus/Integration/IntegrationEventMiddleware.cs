using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lzq.Extensions.EventBus.Integration;

public class IntegrationEventMiddleware<TRequest, TResponse>(
    IServiceProvider serviceProvider,
    ILogger<IntegrationEventMiddleware<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // 1. 判断是否为集成事件
        if (request is not IIntegrationEvent integrationEvent)
        {
            return await next();
        }

        // 2. 调用存储接口
        try
        {
            // 1. 尝试从容器获取存储器（即 Outbox 实现）
            var eventStore = serviceProvider.GetService<IIntegrationEventStore>();

            if (eventStore != null)
            {
                // 情况 A：开启了 Outbox 模式
                logger.LogDebug("[EventBus] 使用 Outbox 存储集成事件: {EventName}", request.GetType().Name);
                await eventStore.SaveAsync(integrationEvent, ct);
            }
            else
            {
                // 情况 B：默认模式，直接发布
                var publisher = serviceProvider.GetService<IIntegrationPublisher>();
                if (publisher != null)
                {
                    logger.LogDebug("[EventBus] 未检测到 Outbox，直接发布集成事件: {EventName}", request.GetType().Name);
                    await publisher.PublishAsync(integrationEvent, ct);
                    return default!;
                }
                else
                {
                    throw new InvalidOperationException($"检测到集成事件 {request.GetType().Name}，但未配置 Outbox 持久化或 IntegrationPublisher。");
                }
            }
            // 集成事件拦截后不再寻找本地 Handler
            return default!;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventBus] 集成事件处理失败: {Message}", ex.Message);
            throw; // 异常会触发外层 TransactionBehavior 的回滚
        }
    }
}