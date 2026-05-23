using Lzq.Core;
using Lzq.Core.Modules;
using Masa.BuildingBlocks.Data;

namespace Lzq.Extensions.ExternalHttpApi;

[DependsOn(typeof(CoreModule))]
public class ExternalHttpApiModule : BaseModule
{
    public override void Configure(ModuleConfigureContext context)
    {
        var currentAssembly = typeof(ExternalHttpApiModule).Assembly;
        MasaApp.TryAddAssemblies(currentAssembly);
    }

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddExternalHttpApis(context.Configuration);
    }
}
