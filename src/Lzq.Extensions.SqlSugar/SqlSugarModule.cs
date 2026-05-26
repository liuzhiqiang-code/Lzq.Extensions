using Lzq.Core;
using Lzq.Core.Modules;

namespace Lzq.Extensions.SqlSugar;

[DependsOn(typeof(CoreModule))]
public class SqlSugarModule : BaseModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddLzqSqlSugar(context.Configuration);
    }
}
