using Lzq.Core;
using Lzq.Core.Modules;
using Masa.BuildingBlocks.Data;

namespace Lzq.Extensions.EventBus;

[DependsOn(typeof(CoreModule))]
public class EventBusModule : BaseModule
{
    public override void Configure(ModuleConfigureContext context)
    {
        var currentAssembly = typeof(EventBusModule).Assembly;
        MasaApp.TryAddAssemblies(currentAssembly);
    }

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddEventBus()
            .AddIntegrationEvent(option =>
            {
                option.UseMemoryOutbox();
                //option.AddRabbitMqPublisher(opt =>
                //{
                //    // RabbitMQ 连接信息从 appsettings.json 读取
                //    // 此处为兜底默认值
                //});
            });
    }
}
