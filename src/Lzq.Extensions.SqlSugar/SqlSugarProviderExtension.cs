using Lzq.Core.Interfaces;
using Lzq.Extensions.SqlSugar.Entities;
using Lzq.Extensions.SqlSugar.SeedData;
using Masa.BuildingBlocks.Data;
using Microsoft.AspNetCore.Http;
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
    public static ISqlSugarClient UseSqlLog(this ISqlSugarClient db, IServiceProvider serviceProvider)
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
    public static ISqlSugarClient UseAuditedField(this ISqlSugarClient db, IHttpContextAccessor httpContextAccessor)
    {
        db.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            // 1. 自动生成主键（Insert 时 Id 为空或 0）
            if (entityInfo.OperationType == DataFilterType.InsertByObject
                && entityInfo.PropertyName == "Id"
                && (oldValue == null || (oldValue is long l && l == 0)))
            {
                entityInfo.SetValue(YitIdHelper.NextId());
                return;
            }

            // 2. Insert：时间字段（CreateTime）
            if (entityInfo.OperationType == DataFilterType.InsertByObject
                && _auditTimeFields.Contains(entityInfo.PropertyName))
            {
                entityInfo.SetValue(DateTime.Now);
                return;
            }

            // 3. Insert：用户字段（Creator）
            if (entityInfo.OperationType == DataFilterType.InsertByObject
                && _auditUserFields.Contains(entityInfo.PropertyName))
            {
                entityInfo.SetValue(GetCurrentUserId(httpContextAccessor));
                return;
            }

            // 4. Update：时间字段（ModificationTime）
            if (entityInfo.OperationType == DataFilterType.UpdateByObject
                && entityInfo.PropertyName == "ModificationTime")
            {
                entityInfo.SetValue(DateTime.Now);
                return;
            }

            // 5. Update：用户字段（Modifier）
            if (entityInfo.OperationType == DataFilterType.UpdateByObject
                && entityInfo.PropertyName == "Modifier")
            {
                entityInfo.SetValue(GetCurrentUserId(httpContextAccessor));
                return;
            }

            // 6. 软删除
            if (entityInfo.OperationType == DataFilterType.DeleteByObject
                && entityInfo.PropertyName == "IsDeleted")
            {
                entityInfo.SetValue(true);
            }
        };

        return db;
    }

    private static string GetCurrentUserId(IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext; // ← 每次执行时动态取
        var currentUser = httpContext?.RequestServices.GetService<ICurrentUser>();
        if (currentUser == null) return "0";
        return currentUser.UserId.IsNullOrWhiteSpace() ? "0" : currentUser.UserId;
    }

    /// <summary>
    /// 查询过滤
    /// </summary>
    public static ISqlSugarClient UseQueryFilter(this ISqlSugarClient db)
    {
        db.QueryFilter.AddTableFilter<IBaseFullEntity>(a => a.IsDeleted == false);
        return db;
    }
}