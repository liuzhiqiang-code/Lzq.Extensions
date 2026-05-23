using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Lzq.Core.Modules;

public class ModuleUseContext
{
    public IApplicationBuilder App { get; }

    public IConfiguration Configuration { get; }

    public IServiceProvider ServiceProvider { get; }

    public ModuleUseContext(IApplicationBuilder app, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        App = app;
        Configuration = configuration;
        ServiceProvider = serviceProvider;
    }
}