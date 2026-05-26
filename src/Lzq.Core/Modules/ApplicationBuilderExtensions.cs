using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Core.Modules;

public static class ApplicationBuilderExtensions
{
    public static async Task AddApplicationAsync<TModule>(
        this WebApplicationBuilder builder)
        where TModule : IModule
    {
        var moduleBuilder = new SerializationModuleBuilder(builder.Services, builder.Configuration);
        moduleBuilder.RegisterSelf(builder.Services);
        moduleBuilder.ConfigureModules<TModule>();
    }

    public static async Task InitializeApplicationAsync(
        this WebApplication app)
    {
        var moduleBuilder = app.Services.GetRequiredService<SerializationModuleBuilder>();
        moduleBuilder.Initialize(app);
    }
}
