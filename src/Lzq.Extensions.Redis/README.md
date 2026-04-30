# Lzq.Extensions.Redis

基于 **FreeRedis** 的 .NET 8 轻量级 Redis 封装库。

## ⚙️ 配置 (appsettings.json)

``` JSON
{
  "Redis": {
    "Prefix": "Lzq:",
    "ConnectionString": "127.0.0.1:6379",
    "IsCluster": false,
    "Sentinels": []
  }
}
```

## 🛠️ 注册服务

``` C#
builder.Services.AddLzqRedis(builder.Configuration);
```

## 💡 核心用法

### 1. 分布式锁 (RAII 模式)

使用 `using` 自动管理锁生命周期，确保异常时安全释放。

``` C#
using (_redis.Lock("resource_key", 10))
{
    // 互斥业务逻辑
}
```

### 2. 高级缓存 (GetOrSetAsync)

一行代码解决：**缓存击穿（并发锁）** 、**穿透（空值处理）和雪崩（随机过期）** 。

``` C#
var data = await _redis.GetOrSetAsync(
    "user_info", 
    async () => await _db.Users.GetAsync(id), 
    TimeSpan.FromHours(1)
);
```

### 3. 基础操作

``` C#
await _redis.SetAsync("key", value, TimeSpan.FromMinutes(10));
var val = await _redis.GetAsync<string>("key");
await _redis.RemoveAsync("key");
```

## 💎 核心优势

- **集群适配**：自动处理 Hash Tag `{}`，支持 Redis Cluster。
- **类型安全**：基于 .NET 8 强类型 Options 验证。
- **健壮重试**：内置指数退避逻辑，缓解高并发锁竞争压力。

---

### ⚠️ 提示

- `Lock` 失败会抛出异常，业务层需捕获。
- 所有 Key 自动带前缀：`Prefix:{key}`。