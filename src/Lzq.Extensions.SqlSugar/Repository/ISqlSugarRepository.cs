using Lzq.Core.Interfaces;
using SqlSugar;
using System.Linq.Expressions;

namespace Lzq.Extensions.SqlSugar.Repository;

public interface ISqlSugarRepository<TEntity> : ISimpleClient<TEntity> where TEntity : class, new()
{
}

public interface ISqlSugarLogicalDeleteRepository<TEntity> : ISqlSugarRepository<TEntity>
    where TEntity : class, IDeleted, new()
{
    void LogicDelete(Expression<Func<TEntity, bool>> exp);
    Task<int> LogicDeleteAsync(Expression<Func<TEntity, bool>> exp);
}
