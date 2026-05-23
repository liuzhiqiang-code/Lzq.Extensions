using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Core.Modules;

public class ModuleConfigureContext
{
    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public ModuleConfigureContext(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }
}