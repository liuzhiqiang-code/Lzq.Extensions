using Lzq.Core;
using Lzq.Core.Modules;

namespace Lzq.Extensions.EventBus;

[DependsOn(typeof(CoreModule))]
public class EventBusModule : BaseModule
{
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
