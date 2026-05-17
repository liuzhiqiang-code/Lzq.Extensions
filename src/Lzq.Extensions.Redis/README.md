# Lzq.Extensions.Redis

基于 **FreeRedis** 的 .NET 8 轻量级 Redis 封装库，提供开箱即用的分布式锁、缓存穿透/击穿/雪崩防护，并支持集群、哨兵、主从等多种部署模式。

---

## ⚙️ 配置 (appsettings.json)

根据部署架构选择对应的配置示例。**每种模式只需填写必要的字段，其他字段可省略或保持默认。**

### 1. 单节点模式

适用于开发环境或简单的单实例 Redis。

json

```
{
  "Redis": {
    "Prefix": "Lzq:",
    "ConnectionString": "127.0.0.1:6379"
  }
}
```

> 可选参数 `IsCluster` 默认 `false`，其他字段无需配置。

### 2. 主从 / 读写分离模式

配置主库和从库，开启读写分离可让读操作分摊到从库。

json

```
{
  "Redis": {
    "Prefix": "Lzq:",
    "ConnectionString": "127.0.0.1:6379",
    "SlaveConnectionStrings": [
      "127.0.0.1:6380",
      "127.0.0.1:6381"
    ],
    "ReadWriteSplitting": true   // 是否开启读写分离，默认 false
  }
}
```

> 仅设置 `SlaveConnectionStrings` 不开启 `ReadWriteSplitting` 时，从库仅作备份；开启后读请求将路由到从库。

### 3. 哨兵模式

通过哨兵自动发现主节点，并支持故障转移。

json

```
{
  "Redis": {
    "Prefix": "Lzq:",
    "ConnectionString": "mymaster",       // 哨兵监控的主节点名称
    "Sentinels": [
      "127.0.0.1:26379",
      "127.0.0.1:26380",
      "127.0.0.1:26381"
    ],
    "SentinelRwSplitting": false          // 哨兵模式下是否开启读写分离
  }
}
```

> - `ConnectionString` 填写哨兵监控的 **master name**，不是 IP 地址。
> - `SentinelRwSplitting` 控制是否将读请求分流到从库，默认 `false`。

### 4. 集群模式

适用于 Redis Cluster 部署。

json

```
{
  "Redis": {
    "Prefix": "Lzq:",
    "ConnectionString": "127.0.0.1:7000",
    "IsCluster": true
  }
}
```

> - `ConnectionString` 只需填写任意一个集群节点的地址，库会自动发现其他节点。
> - 集群模式下 `Sentinels` 和 `SlaveConnectionStrings` 均无效。

### 模式对比

| 模式   | 关键字段     | 适用场景           |
| -------- | -------------- | -------------------- |
| 单节点 | `ConnectionString`             | 开发/测试环境      |
| 主从   | `ConnectionString`, `SlaveConnectionStrings`           | 读写分离、数据备份 |
| 哨兵   | `ConnectionString`（服务名）, `Sentinels` | 自动故障转移       |
| 集群   | `IsCluster = true`             | 大规模数据分片     |

---

## 🛠️ 注册服务

csharp

```
builder.Services.AddLzqRedis(builder.Configuration);
```

注册后可通过 `ILzqRedisClient` 注入使用，内部已封装键前缀、序列化及高级缓存策略。

---

## 💡 核心用法

### 1. 基础操作

csharp

```
// 设置（自动使用 CamelCase JSON 序列化）
await _redis.SetAsync("key", value, TimeSpan.FromMinutes(10));
var val = await _redis.GetAsync<string>("key");
await _redis.RemoveAsync("key");
bool exists = await _redis.ExistsAsync("key");
```

### 2. 分布式锁 (RAII 模式)

基于 FreeRedis 分布式锁，`using` 自动释放。

csharp

```
using (_redis.Lock("resource_key", 10))
{
    // 互斥业务逻辑，锁超时时间 10 秒
}
```

内部包含自动续期（看门狗机制），并可通过 `HandleLostToken` 感知锁丢失。

### 3. 高级缓存 (GetOrSetAsync)

一行代码同时解决缓存击穿、穿透、雪崩。

csharp

```
var data = await _redis.GetOrSetAsync(
    "user_info",
    async () => await _db.Users.GetAsync(id),
    TimeSpan.FromHours(1)
);
```

**内部行为：**

1. 检查缓存，空值标记 `NQ__` 直接返回 null（防穿透）。
2. 尝试获取分布式锁，失败时指数退避重试（最多 3 次，100ms/200ms/300ms）。
3. 获取锁后双重校验。
4. 执行回源函数：

    - 结果为 null → 缓存 `NQ__` 60 秒，返回 null。
    - 结果非 null → 序列化存入 Redis，过期时间增加 0\~300 秒随机抖动（防雪崩）。

---

## 🔑 键格式与集群适配

所有操作键自动包装为：

text

```
{Prefix}:{原始key}
```

例如 `Prefix = "Lzq:"`, key \= "user:1" → 最终键 `Lzq:{user:1}`
**花括号**  **`{}`**  **是 Redis Cluster 哈希标签（hash tag）** ，确保锁键（`Lzq:{user:1}:lock`）与数据键落在同一分片，集群模式下锁机制正常工作。

---

## 🧬 序列化策略

- 全局 `IRedisClient` 配置 **CamelCase** JSON 委托，`Get<T>` / `Set<T>` 直接使用。
- `GetOrSetAsync` 内部使用原生字符串方法手动序列化，避免双重序列化。

---

## ⚠️ 注意事项

- `Lock` 失败会抛出异常，业务层需适当捕获。
- 键前缀由 `LzqRedisClient` 统一管理，**不要**在连接字符串中重复设置 `prefix`。
- 使用哈希标签时，请确保业务 key 本身不含花括号，否则可能影响分片结果。
- 空值缓存过期时间固定为 60 秒，可按需修改源码常量。

---

## 📦 依赖

- **FreeRedis**：高性能 .NET Redis 客户端

---

## 📄 许可证

本项目采用 MIT 许可证，详见仓库根目录 LICENSE 文件。