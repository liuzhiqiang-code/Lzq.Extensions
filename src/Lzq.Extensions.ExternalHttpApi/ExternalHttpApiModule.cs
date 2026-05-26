using Lzq.Core;
using Lzq.Core.Modules;

namespace Lzq.Extensions.ExternalHttpApi;

[DependsOn(typeof(CoreModule))]
public class ExternalHttpApiModule : BaseModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddExternalHttpApis(context.Configuration);
    }
}
