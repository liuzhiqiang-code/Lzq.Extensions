namespace Lzq.Core.Modules;

public interface IModule
{
    void PreConfigureServices(ModuleServiceContext context);
    void ConfigureServices(ModuleServiceContext context);
    void PostConfigureServices(ModuleServiceContext context);

    void OnPreApplicationInitialization(ModuleInitContext context);
    void OnApplicationInitialization(ModuleInitContext context);
    void OnPostApplicationInitialization(ModuleInitContext context);
}