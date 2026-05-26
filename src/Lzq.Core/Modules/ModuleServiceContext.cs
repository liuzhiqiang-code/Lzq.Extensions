using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Core.Modules;

public class ModuleServiceContext
{
    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public ModuleServiceContext(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }
}