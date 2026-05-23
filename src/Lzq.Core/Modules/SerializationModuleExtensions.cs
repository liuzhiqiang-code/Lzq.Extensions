using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Core.Modules;

public static class SerializationModuleExtensions
{
    public static SerializationModuleBuilder SerializationModules(this WebApplicationBuilder builder)
    {
        var moduleBuilder = new SerializationModuleBuilder(builder.Services, builder.Configuration);
        moduleBuilder.RegisterSelf(builder.Services);
        return moduleBuilder;
    }

    public static WebApplication UseSerializationModules(this WebApplication app)
    {
        var builder = app.Services.GetRequiredService<SerializationModuleBuilder>();
        builder.Initialize(app);
        return app;
    }
}