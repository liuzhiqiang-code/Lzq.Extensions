using Lzq.Core.Modules;
using Masa.BuildingBlocks.Data;

namespace Lzq.Extensions.WebApi;

public class WebApiModule : BaseModule
{
    public override  void Configure(ModuleConfigureContext context)
    {
        var currentAssembly = typeof(WebApiModule).Assembly;
        MasaApp.TryAddAssemblies(currentAssembly);
    }

    public override void PostConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddAutoInject(MasaApp.GetAssemblies());
        context.Services
            .AddMasaMinimalAPIs(options =>
            {
                options.DisableTrimMethodPrefix = true;//禁用移除方法前缀(上方 `Get`、`Post`、`Put`、`Delete` 请求的前缀)
                options.MapHttpMethodsForUnmatched = new string[] { "Post" };//当前服务禁用自动注册路由
            });
    }

    public override void OnPreApplicationInitialization(ModuleInitContext context)
    {
    }

    public override void OnPostApplicationInitialization(ModuleInitContext context)
    {
        context.App.MapMasaMinimalAPIs();
    }
}
