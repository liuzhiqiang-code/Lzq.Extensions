using FreeRedis;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Lzq.Extensions.Cache;

public class CacheClient : ICacheClient
{
    private readonly IRedisClient _client;
    private readonly ILogger<CacheClient> _logger;
    private readonly string _keyPrefix;
    private const string NullValueTag = "NQ__";

    public CacheClient(IRedisClient client, ILogger<CacheClient> logger, string prefix = "Lzq:")
    {
        _client = client;
        _logger = logger;
        _keyPrefix = prefix;
    }

    private string FixKey(string key) => $"{_keyPrefix}{key}";

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
            await _client.SetAsync(FixKey(key), value, (int)expiry.Value.TotalSeconds);
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
        {
            throw new Exception($"Failed to acquire lock for: {resourceKey}");
        }
        return lockObj;
    }

    /// <summary>
    /// 解决：缓存雪崩 - 增加随机过期时间
    /// </summary>
    private TimeSpan GetRandomExpiry(TimeSpan baseExpiry)
    {
        // 增加 0-300 秒的随机抖动
        var random = new Random();
        return baseExpiry.Add(TimeSpan.FromSeconds(random.Next(0, 300)));
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> dataRetriever, TimeSpan? expiry = null)
    {
        var fullKey = FixKey(key);
        var finalExpiry = expiry ?? TimeSpan.FromHours(1);

        // 1. 先尝试从缓存读取
        var cachedData = await _client.GetAsync<string>(fullKey);

        // 解决：缓存穿透 (命中空值占位符)
        if (cachedData == NullValueTag) return default;

        if (!string.IsNullOrEmpty(cachedData))
        {
            return JsonSerializer.Deserialize<T>(cachedData);
        }

        // 2. 解决：缓存击穿 (高并发下只允许一个线程去查数据库)
        // 使用分布式锁，锁名与 Key 相关
        using (var @lock = _client.Lock($"{fullKey}:lock", 10))
        {
            if (@lock == null)
            {
                // 没拿到锁的线程进行重试或短暂停顿后再次读取缓存
                await Task.Delay(100);
                return await GetOrSetAsync(key, dataRetriever, expiry);
            }

            // 二次检查，防止在等锁期间另一个线程已经写好了缓存
            cachedData = await _client.GetAsync<string>(fullKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                return cachedData == NullValueTag ? default : JsonSerializer.Deserialize<T>(cachedData);
            }

            // 3. 执行数据源查询（通常是查数据库）
            var data = await dataRetriever();

            if (data == null)
            {
                // 解决：缓存穿透 (数据库没有也存一个短效空值)
                await _client.SetAsync(fullKey, NullValueTag, 60); // 60秒过期
                return default;
            }

            // 4. 解决：缓存雪崩 (存入缓存，并随机偏移过期时间)
            var jitterExpiry = GetRandomExpiry(finalExpiry);
            await _client.SetAsync(fullKey, JsonSerializer.Serialize(data), (int)jitterExpiry.TotalSeconds);

            return data;
        }
    }
}