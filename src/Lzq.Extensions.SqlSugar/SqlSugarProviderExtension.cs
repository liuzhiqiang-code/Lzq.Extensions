using Lzq.Core.Interfaces;
using Lzq.Extensions.SqlSugar.Entities;
using Lzq.Extensions.SqlSugar.SeedData;
using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Yitter.IdGenerator;

namespace Lzq.Extensions.SqlSugar;

public static class SqlSugarProviderExtension
{
    private static readonly HashSet<string> _auditTimeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreationTime", "ModificationTime"
    };

    private static readonly HashSet<string> _auditUserFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Creator", "Modifier"
    };


    /// <summary>
    /// CodeFirst
    /// </summary>
    public static ISqlSugarClient UseCodeFirst(this ISqlSugarClient db, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("UseCodeFirst");
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
                logger?.LogError(ex, "UseCodeFirst 执行失败，实体类型：{EntityType}", type.FullName);
            }
        }
        return db;
    }

    /// <summary>
    /// 种子数据
    /// </summary>
    public static ISqlSugarClient UseSeedData(this ISqlSugarClient db, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SqlSugarSeedData");
        var initializers = serviceProvider.GetServices<ISeedDataInitializer>();

        foreach (var initializer in initializers)
        {
            var type = initializer.GetType();
            try
            {
                initializer.Initialize(db);
                logger.LogInformation("种子数据 {TypeName} 执行成功", type.FullName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "种子数据 {TypeName} 执行失败", type.FullName);
            }
        }
        return db;
    }

    /// <summary>
    /// sql日志
    /// </summary>
    public static SqlSugarProvider UseSqlLog(this SqlSugarProvider db, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("UseSqlLog");
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
    public static SqlSugarProvider UseAuditedField(this SqlSugarProvider db, Func<ICurrentUser?> getCurrentUser)
    {
        db.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            if (entityInfo.PropertyName == "Id" && entityInfo.OperationType == DataFilterType.InsertByObject
            && (oldValue == null || (oldValue is long l && l == 0)))
            {
                var propType = entityInfo.EntityColumnInfo?.PropertyInfo?.PropertyType;
                if (propType == typeof(long))
                    entityInfo.SetValue(YitIdHelper.NextId());
            }
            //inset生效
            if (_auditTimeFields.Contains(entityInfo.PropertyName) && entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                entityInfo.SetValue(DateTime.Now);//修改CreateTime字段
            }
            if (_auditUserFields.Contains(entityInfo.PropertyName) && entityInfo.OperationType == DataFilterType.InsertByObject)
            {
                var currentUser = getCurrentUser();
                var userId = "0";
                if (currentUser != null)
                {
                    userId = currentUser.UserId;
                }
                entityInfo.SetValue(userId);//修改Creator字段
            }
            //update生效
            if (entityInfo.PropertyName == "ModificationTime" && entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                entityInfo.SetValue(DateTime.Now);//修改UpdateTime字段
            }
            if (entityInfo.PropertyName == "Modifier" && entityInfo.OperationType == DataFilterType.UpdateByObject)
            {
                var currentUser = getCurrentUser();
                var userId = "0";
                if (currentUser != null)
                {
                    userId = currentUser.UserId;
                }
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
        db.Context.QueryFilter.AddTableFilter<IBaseFullEntity>(a => a.IsDeleted == false);
        return db;
    }
}