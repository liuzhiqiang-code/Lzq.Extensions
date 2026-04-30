using FreeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lzq.Extensions.Redis;

internal class LzqRedisClient : ILzqRedisClient
{
    private readonly IRedisClient _client;
    private readonly ILogger<LzqRedisClient> _logger;
    private readonly LzqRedisOptions _options; // 直接存储 Options
    private const string NullValueTag = "NQ__";

    public LzqRedisClient(IRedisClient client, ILogger<LzqRedisClient> logger, IOptions<LzqRedisOptions> options)
    {
        _client = client;
        _logger = logger;
        _options = options.Value;
    }

    private string FixKey(string key) => $"{_options.Prefix}{{{key}}}";

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

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> dataRetriever, TimeSpan? expiry = null)
    {
        var fullKey = FixKey(key);
        var lockKey = $"{fullKey}:lock"; // 因为 fullKey 带有 {key}，所以 lockKey 也会落在同个 slot
        var finalExpiry = expiry ?? TimeSpan.FromHours(1);

        const int maxRetry = 3;
        int retryCount = 0;

        while (retryCount < maxRetry)
        {
            // 1. 尝试读取
            var cachedData = await _client.GetAsync<string>(fullKey);
            if (cachedData == NullValueTag) return default;
            if (!string.IsNullOrEmpty(cachedData)) return JsonSerializer.Deserialize<T>(cachedData);

            // 2. 尝试获取锁
            using var @lock = _client.Lock(lockKey, 10);
            if (@lock == null)
            {
                retryCount++;
                await Task.Delay(100 * retryCount); // 指数退避
                continue;
            }

            // 3. 双重检查
            cachedData = await _client.GetAsync<string>(fullKey);
            if (!string.IsNullOrEmpty(cachedData))
            {
                return cachedData == NullValueTag ? default : JsonSerializer.Deserialize<T>(cachedData);
            }

            // 4. 回源
            var data = await dataRetriever();

            if (data == null)
            {
                await _client.SetAsync(fullKey, NullValueTag, 60);
                return default;
            }

            var jitterExpiry = GetRandomExpiry(finalExpiry);
            await _client.SetAsync(fullKey, JsonSerializer.Serialize(data), (int)jitterExpiry.TotalSeconds);
            return data;
        }

        throw new Exception("GetOrSetAsync failed after multiple retries due to lock contention.");
    }

    private TimeSpan GetRandomExpiry(TimeSpan baseExpiry)
    {
        // 使用 Random.Shared 提高性能
        return baseExpiry.Add(TimeSpan.FromSeconds(Random.Shared.Next(0, 300)));
    }
}