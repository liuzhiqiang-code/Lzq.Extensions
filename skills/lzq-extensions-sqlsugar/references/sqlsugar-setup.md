# SqlSugar 初始化与多数据库设置参考 — `Lzq.Extensions.SqlSugar`

本文档详细说明如何配置和使用 `Lzq.Extensions.SqlSugar`，包括环境准备、多数据库连接、切换数据库以及常见问题。

## 1. 环境准备

### 1.1 依赖项

`Lzq.Extensions.SqlSugar` 需要以下 NuGet 包（安装主包时自动引入）：
- **SqlSugarCore** (>= 5.1.4) — ORM 核心库
- **Yitter.IdGenerator** (>= 1.0.14) — 分布式雪花 ID 生成器
- **Lzq.Core** — 提供基础接口（`IEntity`, `IDeleted`, `ICurrentUser`, `IUnitOfWork`）及 Masa BuildingBlocks 集成

### 1.2 应用配置

**appsettings.json** 示例：

```json
{
  "IdGeneratorOptions": {
    "WorkerId": 1
  },
  "DBConfigs": [
    {
      "Tag": "MainDb",
      "DbType": "MySql",
      "ConnectionString": "Server=127.0.0.1;Port=3306;Database=main_db;Uid=root;Pwd=123456;",
      "CommandTimeOut": 30
    },
    {
      "Tag": "LogDb",
      "DbType": "Sqlite",
      "ConnectionString": "Data Source=logs.db;",
      "CommandTimeOut": 30
    }
  ]
}
```

- **IdGeneratorOptions**：可选，默认 WorkerId 为 1。多实例部署时请为每个实例分配不同 WorkerId（0\~63）。
- **DBConfigs**：必填，至少包含一个数据库连接。`Tag` 用于在代码中切换数据库，`DbType` 支持所有 SqlSugar 枚举值（MySql, SqlServer, Sqlite, PostgreSQL, Oracle 等）。

### 1.3 注册服务

在 `Program.cs` 或启动类中按顺序执行：

csharp

```
// 1. 必须先注册 Masa 程序集，以便扫描实体和种子数据
builder.AddLzqMasaAssembly();

// 2. 添加 SqlSugar 服务
builder.Services.AddLzqSqlSugar(builder.Configuration);
```

`AddLzqMasaAssembly()` 由 `Lzq.Core` 提供，用于加载当前 AppDomain 中的所有程序集。`AddLzqSqlSugar` 调用后会：

- 配置雪花 ID 生成器。
- 创建 `ISqlSugarClient`（`SqlSugarScope`，单例）。
- 注册 `ISqlSugarRepository<>` 和 `ISqlSugarLogicalDeleteRepository<>`（瞬态）。
- 执行 CodeFirst 建表、种子数据初始化。
- 启用 SQL 日志、审计 AOP、软删除查询过滤。

## 2. 多数据库配置详解

### 2.1 `DBConfigs` 数组

每个配置项代表一个可独立连接的数据库，所有配置共享同一个 `SqlSugarScope`，通过 `ConfigId`（即 `Tag`）区分。

**重要**：CodeFirst 和种子数据会在**所有配置的数据库**上执行（循环每个 ConnectionConfig）。如果只想在部分数据库上建表，请使用不同的 Service 实例或自定义初始化逻辑。

### 2.2 数据库类型映射

`DbType` 使用 `SqlSugar.DbType` 枚举，常用值：

| 枚举  | 数据库          |
| ------- | ----------------- |
| `MySql` (0)  | MySQL / MariaDB |
| `SqlServer` (1)  | SQL Server      |
| `Sqlite` (2)  | SQLite          |
| `Oracle` (3)  | Oracle          |
| `PostgreSQL` (4)  | PostgreSQL      |
| `Dm` (5)  | 达梦            |
| `Kdbndp` (6)  | 人大金仓        |
| `ClickHouse` (13) | ClickHouse      |

其他枚举值参考 SqlSugar 文档。

### 2.3 获取和使用特定数据库连接

在任何地方注入 `ISqlSugarClient`，然后通过 `Tag` 切换数据库：

``` csharp
public class MultiDbService
{
    private readonly ISqlSugarClient _db;

    public MultiDbService(ISqlSugarClient db) => _db = db;

    public async Task DoWorkAsync()
    {
        // 使用 MainDb
        var mainDb = _db.AsTenant().GetConnection("MainDb");
        var users = await mainDb.Queryable<User>().ToListAsync();

        // 切换到 LogDb
        var logDb = _db.AsTenant().GetConnection("LogDb");
        await logDb.Insertable(new Log { Message = "test" }).ExecuteCommandAsync();
    }
}
```

或者使用 `_db.AsTenant().ChangeDatabase("LogDb")` 切换当前 Scope 的默认连接（谨慎使用，可能影响其他并行操作）。

## 3. Table/Column 命名规则

通过在 `ConfigureExternalServices` 中定义的规则，实体映射到数据库时：

- **表名**：自动转为小写。如果实体类名为 `UserAccount`，映射表名为 `useraccount`。可通过 `[SugarTable("user_account")]` 特性覆盖。
- **列名**：在 `EntityService` 中，`column.DbColumnName` 被转为小写。例如属性 `UserName` 对应列 `username`。可通过 `[SugarColumn(ColumnName = "user_name")]` 覆盖。
- **可空类型**：如果属性的上下文为可空引用类型（如 `string?`），则列自动标记为 `IsNullable = true`。

## 4. CodeFirst 行为

`UseCodeFirst()` 扫描所有实现了 `IEntity` 的非抽象类，并调用 `db.CodeFirst.InitTablesWithAttr(type)` 创建或更新表结构。

- **不会删除字段**：`InitTablesWithAttr` 是增量更新（添加缺失的列），不会删除已有列，避免数据丢失。
- **异常处理**：建表过程中如有异常（如权限不足、类型冲突），会被捕获并记录日志，不影响其他表的创建。
- **执行顺序**：在 `AddLzqSqlSugar` 中，CodeFirst 在 SQL 日志、审计 AOP 之后配置，但在种子数据之前执行。

## 5. 种子数据初始化

所有实现了 `ISeedData<T>` 的类都会被 `UseSeedData()` 发现并执行。执行逻辑：

``` csharp
public virtual void Execute(ISqlSugarClient db)
{
    var data = GetSeedData();
    var tableAny = db.AsTenant().QueryableWithAttr<TEntity>().Any();
    if (data != null && data.Any() && !tableAny)
    {
        db.AsTenant().InsertableWithAttr(data).ExecuteCommand();
    }
}
```

- 只有在对应表为空时才插入种子数据，因此多次启动不会重复写入。
- 使用 `AsTenant()` 保证多租户下数据写入正确的数据库。
- 如果种子数据类需要依赖注入，可以重写 `Execute` 方法，但目前默认基类仅使用 Activator 创建实例，无法使用 DI。

## 6. 服务注册详情

`AddLzqSqlSugar` 内部注册了以下服务：

| 服务接口 | 生命周期 | 说明                                               |
| ---------- | ---------- | ---------------------------------------------------- |
| `ISqlSugarClient`         | 单例     | 全局唯一的 SqlSugarScope 实例                      |
| `ISqlSugarRepository<T>`         | 瞬态     | 继承自 SimpleClient\<T\>，提供单表全方位操作 |
| `ISqlSugarLogicalDeleteRepository<T>`         | 瞬态     | 额外提供 LogicDelete/LogicDeleteAsync 方法         |

如果需要使用事务，可单独注册 `IUnitOfWork` 的实现：

``` csharp
builder.Services.AddScoped<IUnitOfWork, SqlSugarUnitWork>();
```

此步骤**非自动**，需要按需添加。

## 7. 常见问题

### 7.1 启动时提示“没有配置DBConfigs”

`DBConfigs` 配置节不能为空。请检查 `appsettings.json` 是否正确加载，或检查 `configuration.GetSection("DBConfigs")` 存在且格式正确。

### 7.2 雪花 ID 冲突或重复

- 确认每个实例的 `WorkerId` 唯一（0\~63）。
- 检查 `YitIdHelper.SetIdGenerator` 是否被多次调用，默认只会调用一次（`AddLzqSqlSugar` 调用一次）。
- 如果在同一 WorkerId 下同一毫秒内生成过多 ID（大于 4096 个），Yitter 会借用未来时间，确保不重复，但可能导致 ID 趋势递增不严格。

### 7.3 软删除未生效

- 查询过滤依赖于实体实现 `IBaseFullEntity`（继承了 `IDeleted`）或至少实现 `IDeleted`。
- 如果手动拼接 SQL 或使用 `db.Queryable<T>().Where(...)` 且未使用全局过滤器，需确保过滤条件正确。本库通过 `db.Context.QueryFilter.AddTableFilter<IBaseFullEntity>(a => a.IsDeleted == false)` 自动添加全局过滤。
- 删除操作必须通过 AOP 截获的 `DeleteByObject` 方式（如 `db.Deleteable<T>().Where(...)`），而不是直接执行 SQL `DELETE FROM ...`。

### 7.4 审计字段未更新

- 审计依赖 `DataExecuting` AOP，该 AOP 在 `UseAuditedField` 中注册。
- `Creator`/`Modifier` 从 `ICurrentUser` 获取，若未注册 `ICurrentUser` 服务，字段将被设为 `"0"`。确保 `Lzq.Core` 提供了 `ICurrentUser` 的实现，或在 DI 中注册。
- 更新操作必须使用 SqlSugar 的 `UpdateByObject`（如 `db.Updateable(obj).ExecuteCommand()`），批量更新或自定义 SQL 不会触发审计 AOP。

### 7.5 无法找到实体或种子数据

- 确保在 `AddLzqSqlSugar` 之前调用了 `builder.AddLzqMasaAssembly()`，该调用负责加载所有程序集。
- 实体必须实现 `IEntity`（`BaseFullEntity` 已实现），且非抽象类。
- 种子数据类必须实现 `ISeedData<T>` 且非抽象类，`T` 必须是实现了 `IEntity` 的类。

## 8. 自定义扩展

- **修改列名规则**：替换 `ConfigureExternalServices` 中的 `EntityService` 或 `EntityNameService`，但需要修改库源码。
- **禁用审计**：继承 `SqlSugarExtensions` 类或在调用链中移除 `.UseAuditedField()`。
- **增加全局过滤器**：可在应用启动后通过 `ISqlSugarClient` 的 `Context.QueryFilter.AddTableFilter` 添加更多过滤条件。