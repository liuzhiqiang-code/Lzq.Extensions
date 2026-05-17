using FreeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lzq.Extensions.Redis;

internal class LzqRedisClient : ILzqRedisClient
{
    private readonly IRedisClient _client;
    private readonly ILogger<LzqRedisClient> _logger;
    private readonly string _prefix;
    private const string NullValueTag = "NQ__";

    // 统一的 JSON 序列化选项（与注册时的委托保持一致）
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LzqRedisClient(IRedisClient client, ILogger<LzqRedisClient> logger, IOptions<LzqRedisOptions> options)
    {
        _client = client;
        _logger = logger;
        _prefix = options.Value.Prefix;  // 仅保存前缀，不依赖全局 csb.Prefix
    }

    // 键格式化：添加前缀，并用花括号确保集群哈希一致性
    private string FixKey(string key) => $"{_prefix}{{{key}}}";

    public T? Get<T>(string key) => _client.Get<T>(FixKey(key));

    public async Task<T?> GetAsync<T>(string key) => await _client.GetAsync<T>(FixKey(key));

    public void Set<T>(string key, T value, TimeSpan? expiry = null)
    {
        if (expiry.HasValue)
            _client.Set(FixKey(key), value, expiry.Value);
        else
            _client.Set(FixKey(key), value);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        if (expiry.HasValue)
            await _client.SetAsync(FixKey(key), value, expiry.Value); // 直接传递 TimeSpan
        else
            await _client.SetAsync(FixKey(key), value);
    }

    public bool Remove(string key) => _client.Del(FixKey(key)) > 0;

    public async Task<bool> RemoveAsync(string key) => await _client.DelAsync(FixKey(key)) > 0;

    public bool Exists(string key) => _client.Exists(FixKey(key));

    public async Task<bool> ExistsAsync(string key) => await _client.ExistsAsync(FixKey(key));

    public IDisposable Lock(string resourceKey, int timeoutSeconds = 10)
    {
        var lockObj = _client.Lock(FixKey($"lock:{resourceKey}"), timeoutSeconds);
        if (lockObj == null)
            throw new Exception($"Failed to acquire lock for: {resourceKey}");
        return lockObj;
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> dataRetriever, TimeSpan? expiry = null)
    {
        var fullKey = FixKey(key);
        var lockKey = $"{fullKey}:lock";
        var finalExpiry = expiry ?? TimeSpan.FromHours(1);

        const int maxRetry = 3;
        for (int retry = 0; retry < maxRetry; retry++)
        {
            // 1. 尝试读取（原生字符串，不触发序列化委托）
            var cachedData = await _client.GetAsync(fullKey);
            if (cachedData == NullValueTag) return default;
            if (!string.IsNullOrEmpty(cachedData))
                return JsonSerializer.Deserialize<T>(cachedData, _jsonOptions);

            // 2. 获取锁
            using var @lock = _client.Lock(lockKey, 10);
            if (@lock == null)
            {
                await Task.Delay(100 * (retry + 1));
                continue;
            }

            // 3. 双重检查
            cachedData = await _client.GetAsync(fullKey);
            if (!string.IsNullOrEmpty(cachedData))
                return cachedData == NullValueTag ? default : JsonSerializer.Deserialize<T>(cachedData, _jsonOptions);

            // 4. 回源
            var data = await dataRetriever();
            if (data == null)
            {
                // 存储空值标记（原生字符串方法）
                await _client.SetAsync(fullKey, NullValueTag, 60);
                return default;
            }

            var jitterExpiry = GetRandomExpiry(finalExpiry);
            // 存储对象（原生字符串方法，手动序列化）
            await _client.SetAsync(fullKey, JsonSerializer.Serialize(data, _jsonOptions), (int)jitterExpiry.TotalSeconds);
            return data;
        }

        throw new Exception("GetOrSetAsync failed after multiple retries due to lock contention.");
    }

    private TimeSpan GetRandomExpiry(TimeSpan baseExpiry)
    {
        return baseExpiry.Add(TimeSpan.FromSeconds(Random.Shared.Next(0, 300)));
    }
}