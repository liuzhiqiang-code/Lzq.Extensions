using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

public class ValidatorBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators, ILogger<ValidatorBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var typeName = request.GetType().Name;
        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
        var failures = validationResults.SelectMany(r => r.Errors).Where(f => f != null).ToList();

        if (failures.Count != 0)
        {
            logger.LogWarning("[EventBus] 参数校验失败 {CommandName}: {@Errors}", typeName, failures);
            var validationException = new ValidationException(failures);
            throw new MasaValidatorException(validationException.Message);
        }

        return await next();
    }
}