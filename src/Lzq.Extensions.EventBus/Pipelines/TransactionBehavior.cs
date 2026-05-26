using Lzq.Core.Attributes;
using Lzq.Core.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Lzq.Extensions.EventBus.Pipelines;

public class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var type = request.GetType();

        // 1. 排除 Query 或 已经存在事务的情况
        if (IsQueryType(type) || unitOfWork.IsAnyTran())
        {
            return await next();
        }

        // 2. 检查是否有 UnitOfWork 特性
        var unitOfWorkAttr = type.GetCustomAttribute<UnitOfWorkAttribute>();
        if (unitOfWorkAttr == null)
        {
            return await next();
        }

        // 3. 执行事务逻辑
        try
        {
            await unitOfWork.BeginTranAsync(unitOfWorkAttr.IsolationLevel);
            logger.LogDebug("[EventBus] 事务开启: {CommandName}, ContextID: {ContextID}", type.Name, unitOfWork.ContextID);

            var response = await next();

            await unitOfWork.CommitTranAsync();
            logger.LogDebug("[EventBus] 事务提交: {CommandName}, ContextID: {ContextID}", type.Name, unitOfWork.ContextID);

            return response;
        }
        catch (Exception ex)
        {
            await unitOfWork.RollbackTranAsync();
            logger.LogError(ex, "[EventBus] 事务回滚: {CommandName}, ContextID: {ContextID}, 原因: {Message}", type.Name, unitOfWork.ContextID, ex.Message);
            throw;
        }
    }

    private bool IsQueryType(Type type)
    {
        return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition().Name == "IQuery`1");
    }
}