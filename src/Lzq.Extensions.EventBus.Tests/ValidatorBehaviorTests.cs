using FluentValidation;
using FluentValidation.Results;
using Lzq.Extensions.EventBus.Pipelines;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lzq.Extensions.EventBus.Tests;

public class ValidatorBehaviorTests
{
    [Fact]
    public async Task Handle_ValidationFail_ShouldThrow()
    {
        // Arrange
        var validatorMock = new Mock<IValidator<TestCommand>>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Prop", "Error") }));
        var loggerMock = new Mock<ILogger<ValidatorBehavior<TestCommand, Unit>>>();
        var behavior = new ValidatorBehavior<TestCommand, Unit>(
            new[] { validatorMock.Object }, loggerMock.Object);
        var request = new TestCommand();
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        // Act & Assert
        // 由于原代码抛出的是 MasaValidatorException，若没有该类型可引入适配，
        // 这里假设异常为 ValidationException（或自定义），调整为捕获 Exception 验证。
        await Assert.ThrowsAsync<MasaValidatorException>(() => behavior.Handle(request, next, CancellationToken.None));
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Handle_ValidationSuccess_ShouldCallNext()
    {
        var validatorMock = new Mock<IValidator<TestCommand>>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var loggerMock = new Mock<ILogger<ValidatorBehavior<TestCommand, Unit>>>();
        var behavior = new ValidatorBehavior<TestCommand, Unit>(
            new[] { validatorMock.Object }, loggerMock.Object);
        var nextCalled = false;
        RequestHandlerDelegate<Unit> next = _ => { nextCalled = true; return Unit.Task; };

        await behavior.Handle(new TestCommand(), next, CancellationToken.None);

        Assert.True(nextCalled);
    }
}