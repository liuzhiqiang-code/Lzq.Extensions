using Lzq.Core.Attributes;
using Lzq.Core.Interfaces;
using Lzq.Extensions.EventBus.Pipelines;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using System.Data;
using Xunit;

namespace Lzq.Extensions.EventBus.Tests;

public class TransactionBehaviorTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<ILogger<TransactionBehavior<IRequest<Unit>, Unit>>> _loggerMock;
    private readonly TransactionBehavior<IRequest<Unit>, Unit> _behavior;

    public TransactionBehaviorTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<TransactionBehavior<IRequest<Unit>, Unit>>>();
        _behavior = new TransactionBehavior<IRequest<Unit>, Unit>(
            _uowMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_QueryType_ShouldSkipTransaction()
    {
        var query = new TestQuery();
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        await _behavior.Handle(query, next, CancellationToken.None);

        Assert.True(nextCalled);
        _uowMock.Verify(u => u.BeginTranAsync(It.IsAny<IsolationLevel>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoUnitOfWorkAttribute_ShouldSkipTransaction()
    {
        var command = new NoAttributeCommand();
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        await _behavior.Handle(command, next, CancellationToken.None);

        Assert.True(nextCalled);
        _uowMock.Verify(u => u.BeginTranAsync(It.IsAny<IsolationLevel>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingTransaction_ShouldSkip()
    {
        var command = new WithUnitOfWorkCommand();
        _uowMock.Setup(u => u.IsAnyTran()).Returns(true);
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        await _behavior.Handle(command, next, CancellationToken.None);

        Assert.True(nextCalled);
        _uowMock.Verify(u => u.BeginTranAsync(It.IsAny<IsolationLevel>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewTransaction_Success_ShouldCommit()
    {
        var command = new WithUnitOfWorkCommand();
        _uowMock.Setup(u => u.IsAnyTran()).Returns(false);
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        await _behavior.Handle(command, next, CancellationToken.None);

        Assert.True(nextCalled);
        _uowMock.Verify(u => u.BeginTranAsync(IsolationLevel.Serializable), Times.Once);
        _uowMock.Verify(u => u.CommitTranAsync(), Times.Once);
        _uowMock.Verify(u => u.RollbackTranAsync(), Times.Never);
    }

    [Fact]
    public async Task Handle_NewTransaction_Exception_ShouldRollback()
    {
        var command = new WithUnitOfWorkCommand();
        _uowMock.Setup(u => u.IsAnyTran()).Returns(false);
        RequestHandlerDelegate<Unit> next = _ => throw new InvalidOperationException("test");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _behavior.Handle(command, next, CancellationToken.None));

        _uowMock.Verify(u => u.BeginTranAsync(IsolationLevel.Serializable), Times.Once);
        _uowMock.Verify(u => u.CommitTranAsync(), Times.Never);
        _uowMock.Verify(u => u.RollbackTranAsync(), Times.Once);
    }
}

// 测试用请求类型
public record TestQuery : BaseQuery<Unit> { } // 实现 IQuery`1
public record NoAttributeCommand : BaseCommand<Unit> { }

[UnitOfWork(IsolationLevel.Serializable)]
public record WithUnitOfWorkCommand : BaseCommand<Unit> { }