using Lzq.Core.Interfaces;
using SqlSugar;
using System.Data;

namespace Lzq.Extensions.SqlSugar.UnitOfWork;

public class SqlSugarUnitWork(ISqlSugarClient sqlSugarClient) : IUnitOfWork
{
    // 映射 SqlSugar 的 ContextID，方便日志追踪
    public string? ContextID => sqlSugarClient.ContextID.ToString();

    /// <summary>
    /// 开启事务
    /// </summary>
    public async Task BeginTranAsync(IsolationLevel isolationLevel)
    {
        // 建议使用 AsTenant() 以支持多租户下的事务一致性
        await sqlSugarClient.AsTenant().BeginTranAsync(isolationLevel);
    }

    /// <summary>
    /// 提交事务
    /// </summary>
    public async Task CommitTranAsync()
    {
        await sqlSugarClient.AsTenant().CommitTranAsync();
    }

    /// <summary>
    /// 判断当前上下文是否已开启事务
    /// </summary>
    public bool IsAnyTran()
    {
        // 检查 Ado 层的事务状态
        return sqlSugarClient.Ado.IsAnyTran();
    }

    /// <summary>
    /// 回滚事务
    /// </summary>
    public async Task RollbackTranAsync()
    {
        await sqlSugarClient.AsTenant().RollbackTranAsync();
    }
}