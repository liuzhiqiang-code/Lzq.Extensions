namespace Lzq.Extensions.Redis;

public interface ILzqRedisClient
{
    // 基础操作
    T? Get<T>(string key);
    Task<T?> GetAsync<T>(string key);

    void Set<T>(string key, T value, TimeSpan? expiry = null);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    bool Remove(string key);
    Task<bool> RemoveAsync(string key);

    // 高级操作
    bool Exists(string key);
    Task<bool> ExistsAsync(string key);

    // 分布式锁封装
    IDisposable Lock(string resourceKey, int timeoutSeconds = 10);

    /// <summary>
    /// 自动处理击穿、穿透和雪崩的高级获取方法
    /// </summary>
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> dataRetriever, TimeSpan? expiry = null);
}
