using Lzq.Core.Interfaces;
using Masa.BuildingBlocks.Data;
using SqlSugar;
using System.Linq.Expressions;

namespace Lzq.Extensions.SqlSugar.Repository;

/// <summary>
/// 基础仓库
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class SqlSugarRepository<TEntity> : SimpleClient<TEntity>, ISqlSugarRepository<TEntity> where TEntity : class, new()
{
    public SqlSugarRepository()
    {
        base.Context = MasaApp.GetRequiredService<ISqlSugarClient>();
    }

    /// <summary>
    /// 构造函数（用于测试场景指定特定数据库客户端）
    /// </summary>
    /// <param name="client">SqlSugar 客户端实例</param>
    public SqlSugarRepository(ISqlSugarClient client)
    {
        base.Context = client;
    }
}

public class SqlSugarLogicalDeleteRepository<TEntity> : SqlSugarRepository<TEntity>, ISqlSugarLogicalDeleteRepository<TEntity>
    where TEntity : class, IDeleted, new()
{
    public void LogicDelete(Expression<Func<TEntity, bool>> exp)
    {
        Context.Updateable<TEntity>()
            .SetColumns(it => new TEntity() { IsDeleted = true })
            .Where(exp).ExecuteCommand();
    }

    public async Task<int> LogicDeleteAsync(Expression<Func<TEntity, bool>> exp)
    {
        return await Context.Updateable<TEntity>()
            .SetColumns(it => new TEntity() { IsDeleted = true })
            .Where(exp)
            .ExecuteCommandAsync();
    }
}