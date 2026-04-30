using Lzq.Core.Interfaces;
using Lzq.Extensions.SqlSugar.Entities;
using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Lzq.Extensions.SqlSugar;

public static class SqlSugarProviderExtension
{
    private static readonly ILogger logger = MasaApp.GetRequiredService<ILoggerFactory>().CreateLogger("SqlSugarExtensions");
    private static readonly ICurrentUser? currentUser = MasaApp.GetService<ICurrentUser>();

    /// <summary>
    /// CodeFirst
    /// </summary>
    public static ISqlSugarClient UseCodeFirst(this ISqlSugarClient db)
    {
        var loadedAssemblies = MasaApp.GetAssemblies().ToList();

        // 获取所有实现了 IEntity 的非抽象类
        var entityTypes = loadedAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                       typeof(IEntity).IsAssignableFrom(t))
            .ToList();
        foreach (var type in entityTypes)
        {
            try
            {
                db.CodeFirst.InitTablesWithAttr(type);
            }
            catch (Exception ex)
            {
                logger?.LogInformation($"UseCodeFirst：{type.FullName}执行失败:原因是:{ex.Message}");
            }
        }
        return db;
    }

    /// <summary>
    /// 种子数据
    /// </summary>
    public static ISqlSugarClient UseSeedData(this ISqlSugarClient db)
    {
        var loadedAssemblies = MasaApp.GetAssemblies().ToList();
        
        // 获取所有实现了 ISeedData<> 的非抽象类
        var seedDataTypes = loadedAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                       t.GetInterfaces().Any(i => i.IsGenericType &&
                                                  i.GetGenericTypeDefinition() == typeof(ISeedData<>)));
        foreach (var type in seedDataTypes)
        {
            try
            {
                // 创建种子数据实例
                var seedDataInstance = Activator.CreateInstance(type);

                if (seedDataInstance == null)
                {
                    logger?.LogWarning($"无法创建 {type.FullName} 的实例");
                    continue;
                }

                // 获取 Execute 方法
                var executeMethod = type.GetMethod("Execute",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (executeMethod != null)
                {
                    // 执行种子数据的同步方法
                    executeMethod.Invoke(seedDataInstance, new object[] { db });
                    logger?.LogInformation($"种子数据 {type.FullName} 执行成功");
                }

            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"UseSeedData {type.FullName} 执行过程中发生错误");
            }
        }
        return db;
    }

    /// <summary>
    /// sql日志
    /// </summary>
    public static SqlSugarProvider UseSqlLog(this SqlSugarProvider db)
    {
        //调试SQL事件，可以删掉 (要放在执行方法之前)
        db.Aop.OnLogExecuting = (sql, pars) =>
        {
            var nativeSql = UtilMethods.GetNativeSql(sql, pars);
            logger?.LogDebug($"准备执行SQL：【{nativeSql}】");

            //获取无参数化SQL 影响性能只适合调试
            //UtilMethods.GetSqlString(DbType.SqlServer,sql,pars)
        };

        db.Aop.OnLogExecuted = (sql, pars) =>
        {
            var nativeSql = UtilMethods.GetNativeSql(sql, pars);
            logger?.LogInformation($"执行SQL完成：【{nativeSql}】 耗时：{db.Ado.SqlExecutionTime.TotalMilliseconds}ms");
        };

        db.Aop.OnError = (exp) =>//SQL报错
        {
            var nativeSql = UtilMethods.GetNativeSql(exp.Sql, (SugarParameter[])exp.Parametres);
            logger?.LogError(exp, $"执行SQL报错：【{nativeSql}】");
        };
        return db;
    }

    /// <summary>
    /// 字段审计
    /// </summary>
    /// <param name="db"></param>
    public static SqlSugarProvider UseAuditedField(this SqlSugarProvider db)
    {
        db.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            //inset生效
            if ("CreationTime,ModificationTime".Split(',').Contains(entityInfo.PropertyName) && entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                entityInfo.SetValue(DateTime.Now);//修改CreateTime字段
            }
            if ("Creator,Modifier".Split(',').Contains(entityInfo.PropertyName) && entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                var userId = currentUser?.UserId;
                if (userId.IsNullOrWhiteSpace())
                    userId = "0";
                entityInfo.SetValue(userId);//修改Creator字段
            }
            //update生效
            if (entityInfo.PropertyName == "ModificationTime" && entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                entityInfo.SetValue(DateTime.Now);//修改UpdateTime字段
            }
            if (entityInfo.PropertyName == "Modifier" && entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                var userId = currentUser?.UserId;
                if (userId.IsNullOrWhiteSpace())
                    userId = "0";
                entityInfo.SetValue(userId);//修改UpdateTime字段
            }
            // delete生效  (软删除)
            if (entityInfo.PropertyName == "IsDeleted" && entityInfo.OperationType == DataFilterType.DeleteByObject)
            {
                entityInfo.SetValue(true);//修改Delete字段
            }
        };
        return db;
    }

    /// <summary>
    /// 查询过滤
    /// </summary>
    public static SqlSugarProvider UseQueryFilter(this SqlSugarProvider db)
    {
        db.Context.QueryFilter
            .AddTableFilter<IBaseFullEntity>(a => a.IsDeleted == false);
        return db;
    }
}