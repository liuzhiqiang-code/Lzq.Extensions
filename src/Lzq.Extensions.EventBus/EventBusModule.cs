using Lzq.Core;
using Lzq.Core.Modules;
using Lzq.Extensions.EventBus.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus;

[DependsOn(typeof(CoreModule))]
public class EventBusModule : BaseModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddEventBus();

        if (context.Configuration.GetValue<bool>("EventBus:UseOutbox", true))
        {
            context.Services.AddScoped<IIntegrationEventStore, DefaultMemoryEventStore>();
        }
    }
}
