# Redis 配置参考

## 1. 配置节：`Redis`

在 `appsettings.json` 中定义，并由 `LzqRedisOptions` 强类型绑定，自动进行数据注解验证。

| 属性 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `Prefix` | `string` | 是 | 全局 Key 前缀。例如 `"Lzq:"`，则最终 Redis Key 为 `"Lzq:{key}"`。 |
| `ConnectionString` | `string` | 是 | Redis 连接字符串，支持单节点、哨兵、集群。 |
| `IsCluster` | `bool` | 否 | 是否为集群模式。若为 `true`，底层 FreeRedis 会通过 `CLUSTER NODES` 自动发现节点。 |
| `Sentinels` | `string[]?` | 否 | 哨兵地址列表，例如 `["sentinel1:26379", "sentinel2:26379"]`。非空时自动切换为哨兵模式。 |

## 2. 连接模式

| 模式 | 配置方式 |
|------|----------|
| 单节点 | `ConnectionString` 填一个地址即可，`IsCluster=false`。 |
| 哨兵 | `Sentinels` 提供哨兵列表，`ConnectionString` 仍可填写主节点（也可留空）。 |
| 集群 | `IsCluster=true`，`ConnectionString` 填一个节点地址。 |

## 3. 注册与验证

使用 `AddLzqRedis` 方法注册：

```csharp
builder.Services.AddLzqRedis(builder.Configuration);
```

内部流程：

1. 绑定 `Redis` 配置到 `LzqRedisOptions` 并启用 `ValidateOnStart`。
2. 注册 `IRedisClient` 为单例，配置全局 JSON 序列化。
3. 注册 `ILzqRedisClient` 及其实现 `LzqRedisClient` 为单例。

## 4. 序列化

全局使用 `System.Text.Json` 进行序列化与反序列化。复杂对象需确保属性可见（公共 get/set）且无循环引用。

## 5. Hash Tag 与 Key 管理

为了在 Redis 集群中确保相关 Key（如锁 Key 与数据 Key）落在同一 Slot，所有 Key 会包装为 `Prefix:{key}`。例如：

- 数据 Key: `Lzq:{user:123}`
- 对应的锁 Key: `Lzq:{user:123}:lock`

这样无论集群如何分片，锁与数据都在同一 Slot。