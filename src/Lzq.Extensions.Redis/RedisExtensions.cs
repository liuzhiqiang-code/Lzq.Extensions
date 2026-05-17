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
            try
            {
                var csb = ConnectionStringBuilder.Parse(options.ConnectionString);

                RedisClient client = options.IsCluster switch
                {
                    // 集群模式：IsCluster = true
                    true => new RedisClient(new[] { csb }),

                    // 哨兵模式：未开启集群，且 Sentinels 列表不为空
                    false when options.Sentinels?.Length > 0 => new RedisClient(
                        sentinelConnectionString: csb,
                        sentinels: options.Sentinels,
                        rw_splitting: options.SentinelRwSplitting),

                    // 主从模式：已配置 SlaveConnectionStrings（读写分离）
                    // 或单节点模式：未配置任何从库 / 哨兵 / 集群
                    _ => options.SlaveConnectionStrings?.Length > 0
                        ? new RedisClient(csb, options.SlaveConnectionStrings.Select(ConnectionStringBuilder.Parse).ToArray())
                        : new RedisClient(csb)
                };

                // 全局序列化配置（与 JSON 交互时使用 CamelCase）
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                client.Serialize = obj => JsonSerializer.Serialize(obj, jsonOptions);
                client.Deserialize = (json, type) => JsonSerializer.Deserialize(json, type, jsonOptions);
                return client;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize Redis client.", ex);
            }
        });

        // 3. 注册你的 internal 实现类
        // 确保 LzqRedisClient 构造函数注入了 IRedisClient 和 IOptions<LzqRedisOptions>
        services.AddSingleton<ILzqRedisClient, LzqRedisClient>();

        return services;
    }
}
