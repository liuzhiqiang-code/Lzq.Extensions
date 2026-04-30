using Lzq.Extensions.EventBus.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.EventBus.RabbitMq;

public static class RabbitMqEventBusBuilderExtensions
{
    /// <summary>
    /// 添加基于 RabbitMQ 的集成事件发布器
    /// </summary>
    public static IntegrationEventBuilder AddRabbitMqPublisher(
        this IntegrationEventBuilder builder,
        Action<RabbitMqOptions> configure)
    {
        // 1. 注册配置
        builder.Services.Configure(configure);

        // 2. 注册发布器实现
        // 使用 Singleton，因为 RabbitMQ 连接通常建议长连接复用
        builder.Services.AddSingleton<IIntegrationPublisher, RabbitMqPublisher>();

        return builder;
    }
}