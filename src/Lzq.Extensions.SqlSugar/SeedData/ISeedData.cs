using SqlSugar;

namespace Lzq.Extensions.SqlSugar.SeedData;

public interface ISeedData<TEntity> : ISeedDataInitializer
    where TEntity : class, new()
{
    List<TEntity> GetSeedData();
    void Execute(ISqlSugarClient db);

    // 默认实现，使旧类无需修改
    void ISeedDataInitializer.Initialize(ISqlSugarClient db) => Execute(db);
}

public abstract class BaseSeedData<TEntity> : ISeedData<TEntity>
    where TEntity : class, new()
{
    public virtual void Execute(ISqlSugarClient db)
    {
        var data = GetSeedData();
        var tableAny = db.AsTenant().QueryableWithAttr<TEntity>().Any();
        if (data != null && data.Any() && !tableAny)
        {
            db.AsTenant().InsertableWithAttr(data).ExecuteCommand();
        }
    }

    public abstract List<TEntity> GetSeedData();
}