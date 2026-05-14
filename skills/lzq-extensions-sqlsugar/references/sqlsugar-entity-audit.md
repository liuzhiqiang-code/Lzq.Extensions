# SqlSugar 实体审计与软删除参考 — `Lzq.Extensions.SqlSugar`

本文档详细说明 `Lzq.Extensions.SqlSugar` 的自动审计字段填充机制、软删除行为与全局查询过滤器的工作原理，以及如何自定义和使用这些特性。

## 1. 实体基础结构

### 1.1 `BaseFullEntity` 定义

所有需要内置审计和软删除的实体都应继承 `BaseFullEntity`：

```csharp
public abstract class BaseFullEntity : IBaseFullEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public long Id { get; set; } = YitIdHelper.NextId();

    [SugarColumn(ColumnName = "creator")]
    public long Creator { get; set; }

    [SugarColumn(ColumnName = "creation_time")]
    public DateTime CreationTime { get; set; }

    [SugarColumn(ColumnName = "modifier")]
    public long Modifier { get; set; }

    [SugarColumn(ColumnName = "modification_time")]
    public DateTime ModificationTime { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
}
```

`IBaseFullEntity` 继承自 `IEntity` 和 `IDeleted`：

``` csharp
public interface IBaseFullEntity : IEntity, IDeleted
{
    long Creator { get; set; }
    DateTime CreationTime { get; set; }
    long Modifier { get; set; }
    DateTime ModificationTime { get; set; }
}
```

其中 `IEntity` 和 `IDeleted` 由 `Lzq.Core` 提供：

- `IEntity`：标记可持久化实体。
- `IDeleted`：提供 `IsDeleted` 属性，用于软删除。

### 1.2 字段默认值

- **Id**：使用 `YitIdHelper.NextId()` 在实例化时生成雪花 ID，无需手动赋值。
- **Creator** 和 **Modifier**：`long` 类型，默认值为 `0`，审计 AOP 负责在插入/更新时填充当前用户 ID。
- **CreationTime** 和 **ModificationTime**：`DateTime` 类型，默认值为 `DateTime.MinValue`，审计 AOP 负责填充当前时间。
- **IsDeleted**：默认 `false`，逻辑删除后变为 `true`。

## 2. 自动审计 AOP（`UseAuditedField`）

### 2.1 注册位置

在 `AddLzqSqlSugar` 方法中，每个数据库连接都会调用：

``` csharp
db.GetConnection(item.ConfigId).UseSqlLog().UseAuditedField().UseQueryFilter();
```

其中 `UseAuditedField()` 注册 `DataExecuting` 事件。

### 2.2 审计字段填充规则

审计 AOP 根据操作类型（`DataFilterType`）和属性名称动态填充值：

| 操作类型 | 属性名 | 填充值 | 说明                            |
| ---------- | -------- | -------- | --------------------------------- |
| `InsertByObject`         | `CreationTime`       | `DateTime.Now`       | 记录首次创建时间                |
| `InsertByObject`         | `ModificationTime`       | `DateTime.Now`       | 插入时也填充修改时间            |
| `InsertByObject`         | `Creator`       | `ICurrentUser.UserId` 或 `"0"`   | 创建人 ID                       |
| `InsertByObject`         | `Modifier`       | `ICurrentUser.UserId` 或 `"0"`   | 修改人 ID（插入时与创建人相同） |
| `UpdateByObject`         | `ModificationTime`       | `DateTime.Now`       | 更新修改时间                    |
| `UpdateByObject`         | `Modifier`       | `ICurrentUser.UserId` 或 `"0"`   | 更新修改人                      |
| `DeleteByObject`         | `IsDeleted`       | `true`       | 执行逻辑删除                    |

**获取当前用户**：

``` csharp
var userId = currentUser?.UserId;
if (userId.IsNullOrWhiteSpace()) userId = "0";
entityInfo.SetValue(userId);
```

- 如果 DI 容器中注册了 `ICurrentUser` 服务，则从 `UserId` 属性取值。
- 如果未注册，或 `UserId` 为空，则统一填充 `"0"`。

### 2.3 触发条件

审计 AOP 仅当使用 SqlSugar 的**对象操作**方法时生效，包括：

- `Insertable(obj).ExecuteCommand()`
- `Updateable(obj).ExecuteCommand()`
- `Deleteable<T>().Where(...).ExecuteCommand()` （会触发 `DeleteByObject`）

以下情况**不会**触发审计：

- 直接执行 SQL：`Ado.ExecuteCommand("INSERT INTO ...")`
- 使用表达式更新而非对象更新：`Updateable<T>().SetColumns(it => it.Name, "new").Where(...).ExecuteCommand()` 只会触发 `UpdateByColumns`，不会进入 `DataExecuting` 的对象属性检查（本 AOP 未处理 `UpdateByColumns`，修改时间等字段需手动维护）。
- 批量操作若使用实体列表：`Insertable(list).ExecuteCommand()` 会为列表中的每个实体触发 `InsertByObject`。

### 2.4 注意事项

- 当使用 `Updateable<T>().SetColumns(it => new T() { Name = "new" }, ...)` 时，SqlSugar 会检测到 `SetColumns` 提供了完整对象，此时可能触发 `UpdateByObject`，从而更新审计字段。
- 若业务上需要绕过审计（如系统管理员的批量修正），建议直接编写 SQL 或使用 `Updateable<T>().SetColumns(...).IgnoreColumns(true, it => it.Modifier, it => it.ModificationTime)` 显式忽略。

## 3. 软删除与逻辑删除

### 3.1 软删除机制

实体 `IsDeleted` 属性负责标记数据是否已删除。本库在两个方面实现软删除：

1. **查询过滤**：全局查询过滤器自动忽略 `IsDeleted = true` 的数据。
2. **删除转换**：当执行 `Deleteable<T>().Where(...).ExecuteCommand()` 时，AOP 会将操作转换为 `Update IsDeleted = true`，而不是物理删除。

### 3.2 全局查询过滤器

`UseQueryFilter()` 注册了以下全局过滤器：

``` csharp
db.Context.QueryFilter.AddTableFilter<IBaseFullEntity>(a => a.IsDeleted == false);
```

这表示对于任何实现了 `IBaseFullEntity` 的实体（即继承 `BaseFullEntity` 的实体），所有查询都会自动附加 `WHERE is_deleted = false` 条件，包括：

- `Queryable<T>().ToList()`
- `Queryable<T>().First()`
- `Queryable<T>().Count()`
- `SimpleClient` 的所有查询方法（`ISqlSugarRepository<T>` 基于 `SimpleClient`）

**注意**：如果实体仅实现了 `IDeleted` 但未实现 `IBaseFullEntity`，则不会被此全局过滤器覆盖。建议所有需要软删除的实体都实现 `IBaseFullEntity` 或自行添加过滤器。

### 3.3 逻辑删除操作

推荐使用 `ISqlSugarLogicalDeleteRepository<T>` 执行逻辑删除：

``` csharp
public async Task<int> LogicDeleteAsync(Expression<Func<TEntity, bool>> exp)
{
    return await Context.Updateable<TEntity>()
        .SetColumns(it => new TEntity() { IsDeleted = true })
        .Where(exp)
        .ExecuteCommandAsync();
}
```

这个方法直接设置 `IsDeleted = true`，不会触发 AOP（因为不是 `DeleteByObject`），也不受全局过滤器影响（通过 `Updateable` 内部行为可约定是否忽略过滤器，默认更新操作不受过滤器限制）。这也意味着 `Modifier` 和 `ModificationTime` 不会被自动更新——如果需要记录删除人和时间，请手动设置或使用 AOP 的 `DeleteByObject` 方式。

### 3.4 物理删除

如果需要物理删除数据，可使用：

- `Deleteable<T>().Where(...).ExecuteCommand()` —— 会被 AOP 转换为逻辑删除。
- 若要绕过 AOP 实现真正的物理删除，需直接使用 `Ado.ExecuteCommand("DELETE FROM ... WHERE ...")`。

一般建议使用提供的手段控制，避免无意间物理删除。

## 4. 与 Masa App 的集成

审计 AOP 依赖 `ICurrentUser` 服务获取当前登录用户。在基于 `Lzq.Core` 构建的应用中，通常已通过 Masa BuildingBlocks 或自定义中间件注册该服务。确保以下之一成立：

- DI 容器包含 `ICurrentUser` 的实现。
- 若无实现，审计字段将被填充为 `"0"`，不会引发异常。

## 5. 自定义审计行为

如果需要修改审计逻辑（例如使用 `Guid` 类型的用户 ID，或添加更多审计字段），可以采取以下方式：

1. 继承 `BaseFullEntity` 并添加新字段，在 AOP 的 `DataExecuting` 中针对新属性补充设置逻辑（需修改库源码或注册额外的 AOP）。
2. 在应用层通过 `SqlSugar.Aop.DataExecuting` 添加额外的处理器，但要注意顺序：库的 AOP 已注册，额外处理器会在之后执行。

为避免冲突，建议在 `AddLzqSqlSugar` 调用后，通过 `ISqlSugarClient` 实例获取 `SqlSugarScope` 并附加事件：

``` csharp
var sqlSugar = app.Services.GetRequiredService<ISqlSugarClient>();
sqlSugar.Aop.DataExecuting += (oldValue, entityInfo) =>
{
    // 自定义审计逻辑
};
```

## 6. 常见问题

### 6.1 `IsDeleted` 过滤失效

- 确认实体继承自 `BaseFullEntity` 或实现了 `IBaseFullEntity`。
- 如果手动调用 `db.Queryable<T>().ClearFilter()` 清除了过滤器，需要重新添加。
- 直接使用 `db.Ado.SqlQuery<T>("SELECT * FROM ...")` 不会经过过滤器，需自行添加条件。

### 6.2 审计字段未更新

- 检查操作是否使用了对象插入/更新/删除（`InsertByObject` / `UpdateByObject` / `DeleteByObject`），而不是 `Ado.ExecuteCommand`。
- 确认 `ICurrentUser` 服务是否已注册，或接受默认值 `"0"`。
- 如果使用 `Updateable<T>().SetColumns(it => new T(){...}, ...)`，确保 `SetColumns` 包含所有字段（对象模式）才会触发 `UpdateByObject`；如果仅更新部分列（如 `SetColumns(it => it.Name, "value")`），则为 `UpdateByColumns`，不会触发对象级审计。

### 6.3 逻辑删除后如何恢复

直接更新 `IsDeleted = false` 即可恢复：
 
``` csharp
await db.Updateable<T>().SetColumns(it => new T() { IsDeleted = false }).Where(e => e.Id == id).ExecuteCommandAsync();
```

此操作不会自动更新 `Modifier` 和 `ModificationTime`（因为不是 `UpdateByObject` 且在 `DataExecuting` 中未对 `IsDeleted` 的更新做审计）。如需记录恢复操作，建议调用 `Updateable` 的完整对象模式。