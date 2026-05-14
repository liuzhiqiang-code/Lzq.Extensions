---
name: lzq-extensions-redis
description: 使用 Lzq.Extensions.Redis 库集成 Redis 缓存、分布式锁以及高级防击穿/穿透/雪崩策略。适用于 .NET 应用中需要高效缓存读写、并发资源锁定以及保护数据库的缓存模式的场景。
license: Proprietary
compatibility: 需要 .NET 8+、Lzq.Extensions.Redis NuGet 包以及 FreeRedis
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.Redis` 基于 **FreeRedis** 封装，提供线程安全的 Redis 客户端操作、分布式 RAII 锁，以及生产级缓存策略（防击穿、穿透、雪崩）。它通过强类型 Options 绑定配置，适配集群/哨兵模式，并自动为 Key 添加统一前缀，避免多服务冲突。

在以下场景使用本技能：
- 需要为 .NET 应用添加 Redis 缓存层（基础 GET/SET/DEL）。
- 需要实现分布式锁，确保同一资源在同一时刻只被一个实例处理。
- 需要防止热点数据失效导致的缓存击穿、穿透及雪崩。
- 需要配置 Redis 连接字符串、集群拓扑、哨兵等。

## 何时使用

- 用户要求“接入 Redis”、“使用分布式锁”、“缓存数据”等。
- 提到 `Lzq.Extensions.Redis`、`ILzqRedisClient`、`AddLzqRedis`。
- 需要配置 `appsettings.json` 中的 Redis 节。
- 需要解决高并发下数据库压力过大的问题。

## 何时不使用

- 纯前端任务。
- 不使用 Redis 的其他缓存方案（如 MemoryCache）。
- 已经使用其他 Redis 客户端库（如 StackExchange.Redis），且无需本库的额外抽象。

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.Redis
```

### 2. 在 `appsettings.json` 中配置

``` json
{
  "Redis": {
    "Prefix": "Lzq:",
    "ConnectionString": "127.0.0.1:6379",
    "IsCluster": false,
    "Sentinels": []
  }
}
```

`Prefix` 用于为所有 Key 添加统一前缀，避免不同服务间 Key 冲突。`IsCluster` 设为 `true` 可启用集群模式，`Sentinels` 提供哨兵节点列表。

### 3. 注册服务

``` csharp
using Lzq.Extensions.Redis;

builder.Services.AddLzqRedis(builder.Configuration);
```

注册后即可在构造函数中注入 `ILzqRedisClient`。

### 4. 基础缓存操作

``` csharp
public class MyService
{
    private readonly ILzqRedisClient _redis;
    public MyService(ILzqRedisClient redis) => _redis = redis;

    public async Task SetAndGetAsync()
    {
        await _redis.SetAsync("user:1", new { Name = "Alice" }, TimeSpan.FromMinutes(10));
        var user = await _redis.GetAsync<User>("user:1");
        Console.WriteLine(user.Name);
        await _redis.RemoveAsync("user:1");
    }
}
```

所有操作自动加上配置的 `Prefix`，实际 Redis Key 为 `Lzq:{key}`。对于集群，使用 Hash Tag `{}` 保证相关 Key 落在同一 Slot。

### 5. 分布式锁

使用 `using` 自动管理锁的生命周期，避免死锁。

``` csharp
using (_redis.Lock("order_pay", timeoutSeconds: 10))
{
    // 业务逻辑：只有获得锁的实例能执行
}
```

内部基于 `FreeRedis` 的 `Lock` 方法，失败会抛出异常。使用时需捕获并处理。

### 6. 高级缓存策略 (GetOrSetAsync)

一行代码自动处理击穿、穿透和雪崩：

``` csharp
var product = await _redis.GetOrSetAsync(
    "product:123",
    async () => await _db.Products.FindAsync(123),
    TimeSpan.FromMinutes(10)
);
```

机制：

- **防止穿透**：空值被标记为特殊值 `NQ__`，直接返回 `null` 而不再回源。
- **防止击穿**：内部使用分布式锁，多个并发请求只有一个真正回源。
- **防止雪崩**：过期时间在基础值上叠加随机秒数（0\~300秒）。

## 配置参考 (`LzqRedisOptions`)

| 配置项 | 类型 | 默认值 | 说明                                         |
| -------- | ------ | -------- | ---------------------------------------------- |
| `Prefix`       | `string`     | `"Lzq:"`       | Key 前缀，用于隔离不同服务或环境。           |
| `ConnectionString`       | `string`     | 必填   | Redis 连接字符串。集群模式下可只填一个节点。 |
| `IsCluster`       | `bool`     | `false`       | 是否启用集群模式。                           |
| `Sentinels`       | `string[]?`     | 空     | 哨兵节点列表，非空时自动切换为哨兵模式。     |

## 注意事项

- **密钥前缀**：所有 Key 自动添加 `{key}` 包装，便于集群 Hash Tag 计算，确保相关 Key 落到同一 Slot。
- **锁竞争**：`GetOrSetAsync` 在竞争锁失败时会重试最多 3 次，采用指数退避（100ms / 200ms / 300ms）。
- **序列化**：全局使用 `System.Text.Json`，自定义对象必须支持 JSON 序列化。
- **空值处理**：为防止穿透，缓存空值会存储 60 秒，不可覆盖默认过期时间。

## 参考资料

- `references/redis-configuration-reference.md` — 详细配置及连接模式说明。
- `references/redis-usage-patterns.md` — GetOrSetAsync 内部流程、锁竞争与重试机制。