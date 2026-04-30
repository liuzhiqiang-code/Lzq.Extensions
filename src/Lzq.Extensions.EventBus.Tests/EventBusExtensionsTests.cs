using FluentValidation;
using Lzq.Core.Interfaces;
using Lzq.Extensions.EventBus.Integration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Lzq.Extensions.EventBus.Tests;

public class EventBusExtensionsTests
{
    [Fact]
    public void AddEventBus_ShouldRegisterCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IUnitOfWork>()); // 避免 TransactionBehavior 依赖缺失
        services.AddEventBus();

        var provider = services.BuildServiceProvider();

        // 验证 IEventBus
        var bus = provider.GetService<IEventBus>();
        Assert.NotNull(bus);

        // 验证 MediatR 管道已注册（通过检查 Mediator 的行为间接验证，或直接解析 IMediator）
        var mediator = provider.GetService<IMediator>();
        Assert.NotNull(mediator);

        // 验证 IIntegrationEventStore 是否按需（未配置 Outbox，不应注册）
        var store = provider.GetService<IIntegrationEventStore>();
        Assert.Null(store); // 默认不注册，除非调用了 UseMemoryOutbox

        // 验证验证器扫描（至少 FluentValidation 程序集存在）
        // 此处仅做 exist 检查，如无验证器则不会添加，但不影响启动
    }

    [Fact]
    public void UseMemoryOutbox_ShouldRegisterStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        var builder = services.AddEventBus();
        builder.AddIntegrationEvent(integrationBuilder =>
        {
            integrationBuilder.UseMemoryOutbox();
        });

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IIntegrationEventStore>();
        Assert.IsType<DefaultMemoryEventStore>(store);
    }

    // 辅助：若需要验证管道顺序，可创建 IServiceCollection 后检查 MediatR 的内部注册，
    // 这部分略复杂，通常通过行为测试保证。
}