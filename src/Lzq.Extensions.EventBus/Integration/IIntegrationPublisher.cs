namespace Lzq.Extensions.EventBus.Integration;

public interface IIntegrationPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct);
}
