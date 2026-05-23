namespace Lzq.Core.Modules;

public abstract class BaseModule : IModule
{
    public virtual void Configure(ModuleConfigureContext context)
    {
    }

    public virtual void PreConfigureServices(ModuleServiceContext context)
    {
    }

    public virtual void ConfigureServices(ModuleServiceContext context)
    {
    }

    public virtual void PostConfigureServices(ModuleServiceContext context)
    {
    }

    public virtual void OnPreApplicationInitialization(ModuleInitContext context)
    {
    }

    public virtual void OnApplicationInitialization(ModuleInitContext context)
    {
    }

    public virtual void OnPostApplicationInitialization(ModuleInitContext context)
    {
    }
}
