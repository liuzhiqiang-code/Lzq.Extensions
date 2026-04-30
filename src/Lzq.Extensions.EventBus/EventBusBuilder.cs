using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus;

public class EventBusBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    // 核心入口：明确开启“集成事件”配置
    public EventBusBuilder AddIntegrationEvent(Action<IntegrationEventBuilder> configure)
    {
        var builder = new IntegrationEventBuilder(Services);
        configure(builder);
        return this;
    }
}