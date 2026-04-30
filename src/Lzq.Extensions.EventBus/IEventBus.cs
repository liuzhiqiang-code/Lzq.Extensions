using MediatR;

namespace Lzq.Extensions.EventBus;

public interface IEventBus
{
    // 处理带返回值的指令 (Command/Query)
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);

    // 处理无返回值的指令 (Command)
    // 内部消化 MediatR 的 Unit，外部只需 await
    Task SendAsync(IRequest request, CancellationToken ct = default);

    // 发布事件
    Task PublishAsync(ILocalEvent @event, CancellationToken ct = default);
}

public class MediatREventBus(IMediator mediator) : IEventBus
{
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        => mediator.Send(request, ct);

    public async Task SendAsync(IRequest request, CancellationToken ct = default)
    {
        await mediator.Send(request, ct);
    }

    public Task PublishAsync(ILocalEvent @event, CancellationToken ct = default)
        => mediator.Publish(@event, ct);
}