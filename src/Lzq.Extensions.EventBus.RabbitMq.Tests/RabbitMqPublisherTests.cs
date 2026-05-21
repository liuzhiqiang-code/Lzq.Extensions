using Lzq.Extensions.EventBus.Integration;
using Lzq.Extensions.EventBus.RabbitMq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Lzq.Extensions.EventBus.RabbitMq.Tests;

public class RabbitMqPublisherTests : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly Mock<ILogger<RabbitMqPublisher>> _loggerMock;
    private readonly RabbitMqPublisher _publisher;

    private Mock<IConnection> _connectionMock;
    private Mock<IChannel> _channelMock;

    // 控制连接创建的委托，可在测试中动态修改
    private Func<CancellationToken, Task<IConnection>> _createConnectionDelegate;

    public RabbitMqPublisherTests()
    {
        _options = new RabbitMqOptions
        {
            HostName = "unit-test",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/"
        };
        _loggerMock = new Mock<ILogger<RabbitMqPublisher>>();
        _connectionMock = new Mock<IConnection>();
        _channelMock = new Mock<IChannel>();

        // 设置基础连接行为
        _connectionMock.Setup(c => c.IsOpen).Returns(true);
        _connectionMock
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_channelMock.Object);

        // 设置 Channel 的基础方法（可在具体测试中覆盖）
        _channelMock
            .Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _channelMock
            .Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("test-queue", 0, 0));
        _channelMock
            .Setup(c => c.QueueBindAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _channelMock
            .Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // 默认委托：返回已 Mock 的连接
        _createConnectionDelegate = _ => Task.FromResult(_connectionMock.Object);

        // 使用 internal 构造函数注入委托
        _publisher = new RabbitMqPublisher(
            Options.Create(_options),
            _loggerMock.Object,
            _createConnectionDelegate);

        // 重置 Topic 缓存
        typeof(RabbitMqPublisher)
            .GetField("_declaredTopics", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(_publisher, new ConcurrentDictionary<string, bool>());
    }

    [Fact]
    public async Task PublishAsync_ShouldDeclareInfrastructureAndPublish()
    {
        var testEvent = new TestIntegrationEvent();
        var expectedBody = JsonSerializer.Serialize(testEvent);

        byte[]? capturedBody = null;
        BasicProperties? capturedProperties = null;

        _channelMock
            .Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, BasicProperties, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, _, props, body, _) =>
                {
                    capturedBody = body.ToArray();
                    capturedProperties = props;
                })
            .Returns(ValueTask.CompletedTask);

        await _publisher.PublishAsync(testEvent);

        Assert.NotNull(capturedBody);
        Assert.Equal(expectedBody, Encoding.UTF8.GetString(capturedBody!));
        Assert.NotNull(capturedProperties);
        Assert.True(capturedProperties!.Persistent);
        Assert.Equal("application/json", capturedProperties.ContentType);

        _channelMock.Verify(c => c.ExchangeDeclareAsync(
            It.IsAny<string>(), It.IsAny<string>(), true, false,
            It.IsAny<IDictionary<string, object?>>(), false, false,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task PublishAsync_SecondCall_ShouldNotDeclareAgain()
    {
        var testEvent = new TestIntegrationEvent();
        await _publisher.PublishAsync(testEvent);

        _channelMock.Invocations.Clear();
        await _publisher.PublishAsync(testEvent);

        _channelMock.Verify(c => c.ExchangeDeclareAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_ConnectionLost_ShouldReconnect()
    {
        // 1. 模拟当前连接断开
        var closedConnection = new Mock<IConnection>();
        closedConnection.Setup(c => c.IsOpen).Returns(false);
        typeof(RabbitMqPublisher)
            .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(_publisher, closedConnection.Object);

        // 2. 准备新连接及新通道
        var newChannel = new Mock<IChannel>();
        newChannel.Setup(c => c.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        newChannel.Setup(c => c.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("new-queue", 0, 0));
        newChannel.Setup(c => c.QueueBindAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        newChannel.Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var newConnection = new Mock<IConnection>();
        newConnection.Setup(c => c.IsOpen).Returns(true);
        newConnection
            .Setup(c => c.CreateChannelAsync(It.IsAny<CreateChannelOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newChannel.Object);

        // 3. 替换委托，使其返回新连接
        _createConnectionDelegate = _ => Task.FromResult(newConnection.Object);
        typeof(RabbitMqPublisher)
            .GetField("_createConnectionAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(_publisher, _createConnectionDelegate);

        var testEvent = new TestIntegrationEvent();
        await _publisher.PublishAsync(testEvent);

        newChannel.Verify(c => c.BasicPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), true,
            It.IsAny<BasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        _publisher?.Dispose();
    }
}

// 测试用集成事件，允许设置 TopicName
public record TestIntegrationEvent : BaseIntegrationEvent
{
    public override string TopicName { get; } = "TestTopic";
}