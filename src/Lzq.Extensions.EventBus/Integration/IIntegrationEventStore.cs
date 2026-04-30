namespace Lzq.Extensions.EventBus.Integration;

/// <summary>
/// 事件容器：暂存当前请求产生的集成事件
/// </summary>
public interface IIntegrationEventStore
{
    Task SaveAsync(IIntegrationEvent @event, CancellationToken ct = default);
}