using SqlSugar;

namespace Lzq.Extensions.SqlSugar.Repository;

public interface ISqlSugarRepository<TEntity> : ISimpleClient<TEntity> where TEntity : class, new()
{
}
