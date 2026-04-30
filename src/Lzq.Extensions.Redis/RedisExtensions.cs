using FreeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lzq.Extensions.Redis;

public static class RedisExtensions
{
    public static IServiceCollection AddLzqRedis(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. 强类型绑定与验证 (.NET 8 风格)
        services.AddOptions<LzqRedisOptions>()
            .Bind(configuration.GetSection("Redis"))
            .ValidateOnStart();

        // 2. 注册原生 IRedisClient
        services.AddSingleton<IRedisClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LzqRedisOptions>>().Value;

            var client = new RedisClient(options.ConnectionString, options.Sentinels, options.IsCluster);

            // 全局序列化配置：确保对象在分布式存储时的一致性
            client.Serialize = obj => JsonSerializer.Serialize(obj);
            client.Deserialize = (json, type) => JsonSerializer.Deserialize(json, type);

            return client;
        });

        // 3. 注册你的 internal 实现类
        // 确保 LzqRedisClient 构造函数注入了 IRedisClient 和 IOptions<LzqRedisOptions>
        services.AddSingleton<ILzqRedisClient, LzqRedisClient>();

        return services;
    }
}
