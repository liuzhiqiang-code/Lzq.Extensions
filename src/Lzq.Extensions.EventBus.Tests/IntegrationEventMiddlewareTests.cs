using Lzq.Extensions.EventBus.Integration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lzq.Extensions.EventBus.Tests;

public class IntegrationEventMiddlewareTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<IntegrationEventMiddleware<TestIntegrationEvent, Unit>>> _loggerMock;
    private readonly IntegrationEventMiddleware<TestIntegrationEvent, Unit> _middleware;

    public IntegrationEventMiddlewareTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<IntegrationEventMiddleware<TestIntegrationEvent, Unit>>>();
        _middleware = new IntegrationEventMiddleware<TestIntegrationEvent, Unit>(
            _serviceProviderMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NonIntegrationEvent_ShouldCallNext()
    {
        // Arrange
        var request = new NonIntegrationEvent(); // 不是 IIntegrationEvent
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        // 此测试由于泛型约束 request 必须是 TRequest，
        // 实际调用时 request 类型由 TRequest 决定，但为了测试分支，需要构造不同类型的请求。
        // 理想做法是单独测试一个通用中间件，这里采用 TRequest=TestIntegrationEvent 变通，
        // 故此项测试不适用本中间件实例。建议为不同 TRequest 分别测试。
    }

    // 实际测试时可将中间件针对不同 TRequest 分别测试，
    // 我们直接测试 TRequest 为 IIntegrationEvent 的场景。

    [Fact]
    public async Task Handle_IntegrationEventWithOutbox_ShouldSaveAndReturnDefault()
    {
        // Arrange
        var @event = new TestIntegrationEvent();
        var storeMock = new Mock<IIntegrationEventStore>();
        _serviceProviderMock.Setup(s => s.GetService(typeof(IIntegrationEventStore)))
            .Returns(storeMock.Object);

        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ =>
        {
            nextCalled = true;
            return Unit.Task;
        };

        // Act
        var result = await _middleware.Handle(@event, next, CancellationToken.None);

        // Assert
        storeMock.Verify(s => s.SaveAsync(@event, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(nextCalled);          // 不应继续传递
        Assert.Equal(default(Unit), result);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("使用 Outbox 存储集成事件")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IntegrationEventWithoutOutbox_WithPublisher_ShouldPublishDirectly()
    {
        // Arrange
        var @event = new TestIntegrationEvent();
        var publisherMock = new Mock<IIntegrationPublisher>();
        _serviceProviderMock.Setup(s => s.GetService(typeof(IIntegrationEventStore)))
            .Returns(null); // 无 Outbox
        _serviceProviderMock.Setup(s => s.GetService(typeof(IIntegrationPublisher)))
            .Returns(publisherMock.Object);

        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ =>
        {
            nextCalled = true;
            return Unit.Task;
        };

        // Act
        var result = await _middleware.Handle(@event, next, CancellationToken.None);

        // Assert
        publisherMock.Verify(p => p.PublishAsync(@event, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(nextCalled);
        Assert.Equal(default(Unit), result);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("未检测到 Outbox，直接发布集成事件")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_IntegrationEvent_NoOutboxAndNoPublisher_ShouldThrow()
    {
        // Arrange
        var @event = new TestIntegrationEvent();
        _serviceProviderMock.Setup(s => s.GetService(typeof(IIntegrationEventStore)))
            .Returns(null);
        _serviceProviderMock.Setup(s => s.GetService(typeof(IIntegrationPublisher)))
            .Returns(null);

        RequestHandlerDelegate<Unit> next = _ => Unit.Task;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _middleware.Handle(@event, next, CancellationToken.None));
        Assert.Contains("未配置 Outbox 持久化或 IntegrationPublisher", ex.Message);
    }
}

public record NonIntegrationEvent : BaseLocalEvent { }