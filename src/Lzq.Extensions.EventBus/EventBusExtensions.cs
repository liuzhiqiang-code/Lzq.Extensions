using FluentValidation;
using Lzq.Extensions.EventBus.Integration;
using Lzq.Extensions.EventBus.Pipelines;
using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus;

public static class EventBusExtensions
{
    public static EventBusBuilder AddEventBus(this IServiceCollection services)
    {
        var assemblies = MasaApp.GetAssemblies().ToArray();
        // 注册 MediatR 核心服务及 Pipeline 顺序
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(assemblies);

            // 此处可根据需要预留通用的 Pipeline 行为
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(IntegrationEventMiddleware<,>));
        });

        // 自动注册 FluentValidation 验证器
        AssemblyScanner.FindValidatorsInAssemblies(assemblies).ForEach(item =>
        {
            services.AddScoped(item.InterfaceType, item.ValidatorType);
        });

        // 注册核心门面
        services.AddScoped<IEventBus, MediatREventBus>();

        return new EventBusBuilder(services);
    }

    public static IntegrationEventBuilder UseMemoryOutbox(this IntegrationEventBuilder builder)
    {
        // 覆盖默认存储，使用 SqlSugar 持久化实现
        builder.Services.AddScoped<IIntegrationEventStore, DefaultMemoryEventStore>();
        return builder;
    }
}