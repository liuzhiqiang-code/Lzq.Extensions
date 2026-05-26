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

        // 验证 IIntegrationEventStore 不应注册（默认无 Outbox）
        var store = provider.GetService<IIntegrationEventStore>();
        Assert.Null(store);
    }
}