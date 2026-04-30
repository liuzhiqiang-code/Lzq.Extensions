using FreeRedis;
using Lzq.Extensions.Cache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lzq.Extensions.ExternalHttpApi;

public static class CacheExtensions
{
    public static IServiceCollection AddCache(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. 读取配置
        var connectionString = configuration.GetSection("Redis:ConnectionString").Value
                               ?? "127.0.0.1:6379,defaultDatabase=0";
        var prefix = configuration.GetSection("Redis:Prefix").Value ?? "Lzq:";

        // 2. 注册 FreeRedis 实例 (单例)
        var redisClient = new RedisClient(connectionString);
        services.AddSingleton<IRedisClient>(redisClient);

        // 3. 注册封装后的 ICacheClient
        services.AddSingleton<ICacheClient>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<CacheClient>>();
            return new CacheClient(redisClient, logger, prefix);
        });

        return services;
    }
}
