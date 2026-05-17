using Lzq.Core.Interfaces;
using Lzq.Extensions.SqlSugar.Config;
using Lzq.Extensions.SqlSugar.Repository;
using Lzq.Extensions.SqlSugar.SeedData;
using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using System.Reflection;
using Yitter.IdGenerator;

namespace Lzq.Extensions.SqlSugar;

public static class SqlSugarExtensions
{

    public static IServiceCollection AddLzqSqlSugar(this IServiceCollection services, IConfiguration configuration)
    {
        var idGeneratorOptions = configuration.GetSection("IdGeneratorOptions").Get<Yitter.IdGenerator.IdGeneratorOptions>()
            ?? new Yitter.IdGenerator.IdGeneratorOptions(1);
        YitIdHelper.SetIdGenerator(idGeneratorOptions);

        var dBConfigs = configuration.GetSection("DBConfigs").Get<List<DBConfig>>()
            ?? throw new MasaArgumentException("没有配置DBConfigs");
        var connectionConfigs = new List<ConnectionConfig>();
        foreach (var item in dBConfigs)
        {
            connectionConfigs.Add(new ConnectionConfig
            {
                ConfigId = item.Tag,
                DbType = item.DbType,
                ConnectionString = item.ConnectionString,
                IsAutoCloseConnection = true,
                ConfigureExternalServices = ConfigureExternalServices,
            });
        }
        // 创建 SqlSugarScope 但不传入初始化回调
        SqlSugarScope sqlSugar = new SqlSugarScope(connectionConfigs);
        services.AddSingleton<ISqlSugarClient>(sqlSugar);

        // 注册种子数据等
        var assemblies = MasaApp.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            var seedTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ISeedDataInitializer).IsAssignableFrom(t));
            foreach (var type in seedTypes)
            {
                services.AddTransient(typeof(ISeedDataInitializer), type);
            }
        }

        var serviceProvider = services.BuildServiceProvider();

        foreach (var item in connectionConfigs)
        {
            var conn = sqlSugar.GetConnection(item.ConfigId);
            conn.UseSqlLog(serviceProvider);
            conn.UseAuditedField(() => MasaApp.GetService<ICurrentUser>());
            conn.UseQueryFilter();
        }

        sqlSugar.UseCodeFirst(serviceProvider);
        sqlSugar.UseSeedData(serviceProvider);

        services.AddTransient(typeof(ISqlSugarRepository<>), typeof(SqlSugarRepository<>));

        return services;
    }

    private static ConfigureExternalServices ConfigureExternalServices =>
         new ConfigureExternalServices()
         {
             //注意:  这儿AOP设置不能少   Nullable类型自动数据库变可空类型
             EntityService = (type, column) =>
             {
                 if (column.IsPrimarykey == false && new NullabilityInfoContext()
                    .Create(type).WriteState is NullabilityState.Nullable)
                 {
                     column.IsNullable = true;
                 }
                 column.DbColumnName = column.DbColumnName?.ToLower();//转小写
             },
             EntityNameService = (type, entity) =>
             {
                 entity.DbTableName = entity.DbTableName?.ToLower();//转小写
             },
         };
}