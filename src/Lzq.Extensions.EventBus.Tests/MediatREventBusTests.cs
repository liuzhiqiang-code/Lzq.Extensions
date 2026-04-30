using MediatR;
using Moq;
using Xunit;

namespace Lzq.Extensions.EventBus.Tests;

public class MediatREventBusTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly MediatREventBus _eventBus;

    public MediatREventBusTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _eventBus = new MediatREventBus(_mediatorMock.Object);
    }

    [Fact]
    public async Task SendAsync_WithResponse_ShouldDelegateToMediator()
    {
        var request = new TestCommand<string>();
        var expected = "result";
        _mediatorMock.Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _eventBus.SendAsync(request);

        Assert.Equal(expected, result);
        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithoutResponse_ShouldDelegate()
    {
        var request = new TestCommand();

        // Setup 非泛型 Send(IRequest, CancellationToken) 返回 Task<object?>
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<object?>(null));

        await _eventBus.SendAsync(request);

        _mediatorMock.Verify(
            m => m.Send(It.Is<IRequest>(r => r == request), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ShouldDelegate()
    {
        var localEvent = new TestLocalEvent();

        // Setup Publish(ILocalEvent, CancellationToken) 返回 Task
        _mediatorMock
            .Setup(m => m.Publish(It.IsAny<ILocalEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _eventBus.PublishAsync(localEvent);

        _mediatorMock.Verify(
            m => m.Publish(It.Is<ILocalEvent>(e => e == localEvent), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public record TestCommand : BaseCommand { }
public record TestCommand<T> : BaseCommand<T> { }
public record TestLocalEvent : BaseLocalEvent { }