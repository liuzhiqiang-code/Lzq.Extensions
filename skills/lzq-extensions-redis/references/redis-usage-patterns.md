# Redis 使用模式 — GetOrSetAsync 详解

## 1. 方法签名

```csharp
Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> dataRetriever, TimeSpan? expiry = null);
```

## 2. 核心流程

1. **读缓存**：尝试从 Redis 读取 Key。

    - 若命中特殊空值标记 `NQ__`，直接返回 `default`。
    - 若命中有效数据，反序列化并返回。
2. **争抢锁**：未命中时，尝试获取分布式锁 `{fullKey}:lock`，超时 10 秒。

    - 若获取失败，重试最多 3 次，每次等待 `100ms * 重试次数`（指数退避）。
3. **双重检查**：获得锁后再次读取缓存，避免在等待锁期间其他线程已写入。
4. **回源**：调用 `dataRetriever` 获取最新数据。

    - 若返回 `null`，则缓存空值标记 60 秒，返回 `default`。
    - 若返回数据，序列化后写入缓存。
5. **随机过期**：写入缓存时，在传入的 `expiry` 基础上增加 `0~300` 秒随机值，防止缓存同时失效导致雪崩。

## 3. 防雪崩实现

``` csharp
private TimeSpan GetRandomExpiry(TimeSpan baseExpiry)
{
    return baseExpiry.Add(TimeSpan.FromSeconds(Random.Shared.Next(0, 300)));
}
```

## 4. 锁竞争与重试

- 最大重试次数：3
- 退避策略：`100ms * (retryCount + 1)`
- 若 3 次后仍未获取锁，抛出 `Exception`，由调用方处理。

## 5. 注意事项

- **不适合极高并发**：重试次数有限，极端情况下可能失败，此时应检查业务是否允许缓存直接回源。
- **空值处理**：数据源返回 `null` 时，缓存空标记的 TTL 固定为 60 秒，不可配置。
- **序列化性能**：复杂对象序列化可能影响性能，建议缓存简单 DTO。