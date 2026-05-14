---
name: lzq-extensions-sqlsugar
description: 使用 Lzq.Extensions.SqlSugar 库快速接入 SqlSugar ORM，集成雪花 ID、审计字段、软删除、逻辑删除、种子数据、多数据库支持以及事务管理。适用于 .NET 应用中需要与 MySQL、SqlServer、PostgreSQL 等数据库交互，并希望自动处理基础字段和配置驱动多租户的场景。
license: Proprietary
compatibility: 需要 .NET 8+、Lzq.Extensions.SqlSugar NuGet 包、SqlSugarCore 5.1.4+、Yitter.IdGenerator 以及 Lzq.Core 基础库
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.SqlSugar` 基于 **SqlSugarCore** 和 **Yitter.IdGenerator** 封装，为 .NET 应用提供开箱即用的 ORM 能力。它通过配置驱动的多数据库连接、雪花 ID 自动生成、审计字段（创建/修改人/时间）自动填充、软删除查询过滤、逻辑删除以及种子数据初始化等功能，简化数据访问层的开发。

在以下场景使用本技能：
- 需要快速接入 SqlSugar 并自动管理数据库连接（支持多库/多租户）。
- 需要实体自动具备雪花 ID、创建/修改审计和软删除能力，减少重复代码。
- 需要通过配置文件切换数据库类型和连接字符串。
- 需要自动执行 CodeFirst 建表和种子数据初始化。
- 需要统一的事务管理和逻辑删除支持。

## 何时使用

- 用户要求“接入 SqlSugar”、“配置多数据库”、“自动生成雪花 ID”、“添加软删除”等。
- 提到 `Lzq.Extensions.SqlSugar`、`AddLzqSqlSugar`、`BaseFullEntity`、`ISeedData`。
- 需要实体继承 `BaseFullEntity` 以获得内置字段。
- 需要使用 `ISqlSugarRepository<T>` 进行基础 CRUD。

## 何时不使用

- 纯内存数据操作或使用其他 ORM（如 EFCore、Dapper）。
- 不需要审计字段、软删除或雪花 ID，偏好原生 SqlSugar 配置。
- 未使用 Masa BuildingBlocks 或 Lzq.Core 基础库（依赖环境未满足）。

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.SqlSugar
```

包内已包含以下传递依赖：

- `SqlSugarCore` (5.1.4+)
- `Yitter.IdGenerator` (雪花 ID 实现)
- `Lzq.Core` (基础接口与 Masa 基础设施)

### 2. 配置 `appsettings.json`

``` json
{
  "IdGeneratorOptions": {
    "WorkerId": 1
  },
  "DBConfigs": [
    {
      "Tag": "MainDb",
      "DbType": "MySql",
      "ConnectionString": "Server=localhost;Port=3306;Database=app_db;Uid=root;Pwd=pass;",
      "CommandTimeOut": 30
    },
    {
      "Tag": "LogDb",
      "DbType": "MySql",
      "ConnectionString": "Server=localhost;Port=3306;Database=log_db;Uid=root;Pwd=pass;",
      "CommandTimeOut": 30
    }
  ]
}
```

- `IdGeneratorOptions.WorkerId`：雪花算法的工作机器 ID（0-31），每个服务实例应唯一。
- `DBConfigs`：数据库配置数组，`Tag` 为连接标识，`DbType` 支持 MySql、SqlServer、Sqlite、PostgreSQL 等，`CommandTimeOut` 单位为秒。

### 3. 注册服务

在 `Program.cs` 中，**必须先注册程序集再添加 SqlSugar**：

``` csharp
using Lzq.Extensions.SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// 注册 Masa 程序集（Lzq.Core 提供）
builder.AddLzqMasaAssembly();

// 添加 SqlSugar 服务
builder.Services.AddLzqSqlSugar(builder.Configuration);
```

内部行为：

- 根据 `DBConfigs` 创建 `SqlSugarScope`，管理多连接。
- 自动调用 `UseCodeFirst()`、`UseSeedData()`、`UseSqlLog()`、`UseAuditedField()` 和 `UseQueryFilter()`。
- 注册 `ISqlSugarClient`（单例）和 `ISqlSugarRepository<>`（瞬态）。

### 4. 定义实体

实体必须继承 `BaseFullEntity`，即可自动获得以下字段（小写列名）：

``` csharp
using Lzq.Extensions.SqlSugar.Entities;

public class User : BaseFullEntity
{
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; }

    [SugarColumn(ColumnName = "email")]
    public string Email { get; set; }
}
```

字段说明：

- `id`：雪花 ID（`long` 类型），自动填充。
- `creator` / `creation_time`：创建人和创建时间，插入时自动赋值。
- `modifier` / `modification_time`：修改人和修改时间，更新时自动赋值。
- `is_deleted`：软删除标志，查询时自动过滤 `is_deleted = false`。

### 5. 基础 CRUD

通过注入 `ISqlSugarRepository<T>` 使用：

``` csharp
public class UserService
{
    private readonly ISqlSugarRepository<User> _repo;

    public UserService(ISqlSugarRepository<User> repo) => _repo = repo;

    public async Task AddUserAsync(string name, string email)
    {
        var user = new User { Name = name, Email = email };
        // Id、Creator、CreationTime 等自动填充
        await _repo.InsertAsync(user);
    }

    public async Task<User> GetByIdAsync(long id)
    {
        // 自动过滤 IsDeleted = false
        return await _repo.GetByIdAsync(id);
    }

    public async Task UpdateUserAsync(User user)
    {
        // Modifier、ModificationTime 自动更新
        await _repo.UpdateAsync(user);
    }
}
```

### 6. 逻辑删除

对于实现了 `IDeleted` 接口的实体，可使用 `ISqlSugarLogicalDeleteRepository<T>` 进行逻辑删除（设置 `IsDeleted = true` 而不物理删除）：

``` csharp
public class UserService
{
    private readonly ISqlSugarLogicalDeleteRepository<User> _logicalRepo;

    public async Task DeleteUserAsync(long userId)
    {
        await _logicalRepo.LogicDeleteAsync(u => u.Id == userId);
    }
}
```

### 7. 事务

使用 `IUnitOfWork`（实现 `SqlSugarUnitWork`）管理事务：

``` csharp
public class OrderService
{
    private readonly ISqlSugarClient _db;
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(ISqlSugarClient db, IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task CreateOrderAsync(Order order, OrderItem item)
    {
        await _unitOfWork.BeginTranAsync(IsolationLevel.ReadCommitted);
        try
        {
            await _db.Insertable(order).ExecuteCommandAsync();
            await _db.Insertable(item).ExecuteCommandAsync();
            await _unitOfWork.CommitTranAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTranAsync();
            throw;
        }
    }
}
```

### 8. 种子数据

创建种子数据类实现 `ISeedData<T>`：

``` csharp
public class UserSeedData : BaseSeedData<User>
{
    public override List<User> GetSeedData()
    {
        return new List<User>
        {
            new User { Name = "Admin", Email = "admin@example.com" }
        };
    }
}
```

应用启动时，`UseSeedData()` 会自动扫描所有实现了 `ISeedData<>` 的类，并在对应表为空时插入数据。无需手动调用。

### 9. 代码优先与 SQL 日志

- **CodeFirst**：`UseCodeFirst()` 会在启动时自动创建所有继承 `IEntity` 的实体表（使用 `InitTablesWithAttr`）。
- **SQL 日志**：所有 SQL 执行前（Debug 级别）和执行后（Information 级别，含耗时）均会记录日志，执行出错时记录 Error。

## 配置参考

### 雪花 ID 配置 (`IdGeneratorOptions`)

| 属性 | 类型 | 默认值 | 说明                                                 |
| ------ | ------ | -------- | ------------------------------------------------------ |
| `WorkerId`     | `ushort`     | `1`       | 工作机器 ID (0\~63)，分布式部署时每个实例必须唯一 |

### 数据库配置 (`DBConfigs`)

| 属性 | 类型 | 默认值 | 说明                                                                |
| ------ | ------ | -------- | --------------------------------------------------------------------- |
| `Tag`     | `string`     | 必填   | 连接标识，用于切换数据库（如 `MySqlConnection`）                                     |
| `DbType`     | `DbType`     | 必填   | 数据库类型，枚举值：MySql, SqlServer, Sqlite, Oracle, PostgreSQL 等 |
| `ConnectionString`     | `string`     | 必填   | 数据库连接字符串                                                    |
| `CommandTimeOut`     | `int`     | `30`       | 命令超时时间（秒）                                                  |

## 实体审计与软删除机制

### 自动审计字段

在实体执行 `InsertByObject` 时：

- `CreationTime` 和 `ModificationTime` 设为当前时间。
- `Creator` 和 `Modifier` 从 `ICurrentUser.UserId` 取值，若为空则设为 `"0"`。

在实体执行 `UpdateByObject` 时：

- `ModificationTime` 设为当前时间。
- `Modifier` 更新为当前用户 ID。

### 软删除

- 基础实体 `BaseFullEntity` 实现 `IDeleted`，包含 `IsDeleted` 属性。
- 全局查询过滤器自动添加 `WHERE is_deleted = false`，查询时只返回未软删除数据。
- 删除操作（`DeleteByObject`）时，AOP 会将 `IsDeleted` 设置为 `true`，执行逻辑删除。
- 可通过 `ISqlSugarLogicalDeleteRepository.LogicDelete()` 或直接使用 `Updateable` 手动逻辑删除。

## 多数据库支持

- 配置多个 `DBConfigs` 条目，每个对应一个 `Tag`。
- 通过 `ISqlSugarClient` 的 `AsTenant().ChangeDatabase(tag)` 或 `db.GetConnection(tag)` 切换数据库。
- 默认情况下，所有实体共享同一个 `SqlSugarScope`，CodeFirst 和种子数据会在所有配置的数据库连接上执行。

## 注意事项

- **程序集注册**：必须在 `AddLzqSqlSugar` 前调用 `builder.AddLzqMasaAssembly()`，否则无法扫描到实体和种子数据类。
- **雪花 ID**：依赖 `YitIdHelper`，WorkerId 必须唯一，避免 ID 冲突。
- **数据库小写列名**：通过 `ConfigureExternalServices` 强制列名和表名转为小写，确保跨数据库一致性。
- **AOP 审计依赖** **`ICurrentUser`**：若应用中未注册 `ICurrentUser` 服务，`Creator`/`Modifier` 会被设为 `"0"`，不会抛异常。
- **种子数据执行时机**：在应用启动时同步执行，数据量大时可能影响启动速度。
- **事务 API**：`IUnitOfWork` 使用 `AsTenant()` 以支持多租户事务，确保在一个租户上下文中操作。
- **SQL 日志级别**：Debug 级别可能包含敏感数据（SQL 参数），生产环境建议将日志级别调整为 Information 或关闭 SQL 日志。
- **软删除支持**：逻辑删除仅适用于实现了 `IDeleted` 的实体，若使用原生 SqlSugar 方法物理删除，则会真正删除数据。

## 参考资料

- `references/sqlsugar-setup.md` — 详细初始化和多库切换示例。
- `references/sqlsugar-entity-audit.md` — 审计字段、软删除与查询过滤的工作原理。
- `references/sqlsugar-seed-data.md` — 种子数据编写指南与执行顺序。