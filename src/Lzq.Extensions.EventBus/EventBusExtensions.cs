using FluentValidation;
using Lzq.Extensions.EventBus.Integration;
using Lzq.Extensions.EventBus.Pipelines;
using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus;

public static class EventBusExtensions
{
    public static void AddEventBus(this IServiceCollection services)
    {
        var assemblies = MasaApp.GetAssemblies().ToArray();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(assemblies);

            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(IntegrationEventMiddleware<,>));
        });

        AssemblyScanner.FindValidatorsInAssemblies(assemblies).ForEach(item =>
        {
            services.AddScoped(item.InterfaceType, item.ValidatorType);
        });

        services.AddScoped<IEventBus, MediatREventBus>();
    }
}