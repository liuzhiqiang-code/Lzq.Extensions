using Lzq.Core;
using Lzq.Core.Modules;
using Masa.BuildingBlocks.Data;

namespace Lzq.Extensions.SqlSugar;

[DependsOn(typeof(CoreModule))]
public class SqlSugarModule : BaseModule
{
    public override void Configure(ModuleConfigureContext context)
    {
        var currentAssembly = typeof(SqlSugarModule).Assembly;
        MasaApp.TryAddAssemblies(currentAssembly);
    }

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddLzqSqlSugar(context.Configuration);
    }
}
