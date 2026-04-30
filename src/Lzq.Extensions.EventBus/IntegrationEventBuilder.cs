using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus;

public class IntegrationEventBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}