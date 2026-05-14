using Lzq.Extensions.SqlSugar.Config;
using Lzq.Extensions.SqlSugar.Repository;
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
        var idGeneratorOptions = configuration.GetSection("IdGeneratorOptions").Get<IdGeneratorOptions>()
            ?? new IdGeneratorOptions(1);
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
        SqlSugarScope sqlSugar = new SqlSugarScope(connectionConfigs, db =>
        {
            foreach (var item in connectionConfigs)
            {
                db.GetConnection(item.ConfigId).UseSqlLog().UseAuditedField().UseQueryFilter();
            }
        });

        sqlSugar.UseCodeFirst().UseSeedData();

        services.AddSingleton<ISqlSugarClient>(sqlSugar);
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