using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Lzq.Extensions.SqlSugar.SeedData;

public interface ISeedData<TEntity> : ISeedDataInitializer
    where TEntity : class, new()
{
    List<TEntity> GetSeedData();
    void Execute(ISqlSugarClient db, ILogger logger);

    // 默认实现，使旧类无需修改
    void ISeedDataInitializer.Initialize(ISqlSugarClient db,ILogger logger) => Execute(db, logger);
}

public abstract class BaseSeedData<TEntity> : ISeedData<TEntity>
    where TEntity : class, new()
{
    public virtual void Execute(ISqlSugarClient db, ILogger logger)
    {
        try
        {
            logger.LogInformation("开始执行种子数据初始化，实体类型：{EntityType}", typeof(TEntity).FullName);
            var data = GetSeedData();
            var tableAny = db.AsTenant().QueryableWithAttr<TEntity>().Any();
            if (data != null && data.Any() && !tableAny)
            {
                db.AsTenant().InsertableWithAttr(data).ExecuteCommand();
            }
            logger.LogInformation("种子数据初始化完成，实体类型：{EntityType}", typeof(TEntity).FullName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SeedData Error");
        }
    }

    public abstract List<TEntity> GetSeedData();
}