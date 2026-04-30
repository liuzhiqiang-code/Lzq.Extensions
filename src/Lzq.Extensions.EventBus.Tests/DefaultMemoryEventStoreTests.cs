using Lzq.Extensions.EventBus.Integration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;
using System.Reflection;
using Xunit;

namespace Lzq.Extensions.EventBus.Tests;

public class DefaultMemoryEventStoreTests
{
    private readonly DefaultMemoryEventStore _store;
    private readonly Mock<ILogger<DefaultMemoryEventStore>> _loggerMock;

    public DefaultMemoryEventStoreTests()
    {
        _loggerMock = new Mock<ILogger<DefaultMemoryEventStore>>();
        _store = new DefaultMemoryEventStore(_loggerMock.Object);
    }

    [Fact]
    public async Task SaveAsync_ShouldEnqueueEvent()
    {
        // Arrange
        var testEvent = new TestIntegrationEvent();
        var queueField = typeof(DefaultMemoryEventStore)
            .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance);
        var queue = (ConcurrentQueue<IIntegrationEvent>)queueField!.GetValue(_store)!;

        // Act
        await _store.SaveAsync(testEvent);

        // Assert
        Assert.True(queue.TryPeek(out var peeked));
        Assert.Same(testEvent, peeked);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[EventBus] 集成事件已进入内存队列")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

// 辅助测试事件
public record TestIntegrationEvent : BaseIntegrationEvent { }