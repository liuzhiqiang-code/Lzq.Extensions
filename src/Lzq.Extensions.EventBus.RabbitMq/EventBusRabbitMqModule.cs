using Lzq.Core.Modules;
using Lzq.Extensions.EventBus;
using Lzq.Extensions.EventBus.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus.RabbitMq;

[DependsOn(typeof(EventBusModule))]
public class EventBusRabbitMqModule : BaseModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.Configure<RabbitMqOptions>(
            context.Configuration.GetSection("RabbitMq"));
        context.Services.AddSingleton<IIntegrationPublisher, RabbitMqPublisher>();
    }
}
