using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Core.Modules;

public class ModuleServiceContext
{
    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }
    public IServiceProvider ServiceProvider { get; }

    public ModuleServiceContext(IServiceCollection services, IConfiguration configuration,IServiceProvider serviceProvider)
    {
        Services = services;
        Configuration = configuration;
        ServiceProvider = serviceProvider;
    }
}