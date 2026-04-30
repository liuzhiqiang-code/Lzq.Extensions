using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Lzq.Extensions.EventBus.Integration;

/// <summary>
/// 默认实现：基于内存队列（不具备持久化能力，仅用于简单场景或占位）
/// </summary>
public class DefaultMemoryEventStore(ILogger<DefaultMemoryEventStore> logger) : IIntegrationEventStore
{
    private readonly ConcurrentQueue<IIntegrationEvent> _queue = new();

    public Task SaveAsync(IIntegrationEvent @event, CancellationToken ct = default)
    {
        _queue.Enqueue(@event);
        logger.LogDebug("[EventBus] 集成事件已进入内存队列: {EventName}", @event.GetType().Name);
        return Task.CompletedTask;
    }
}