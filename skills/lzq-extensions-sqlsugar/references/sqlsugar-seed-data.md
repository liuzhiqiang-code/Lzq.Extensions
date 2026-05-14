# SqlSugar 种子数据参考 — `Lzq.Extensions.SqlSugar`

本文档详细说明 `Lzq.Extensions.SqlSugar` 中种子数据的编写、注册、执行机制和最佳实践，帮助您在应用启动时自动初始化基础数据。

## 1. 种子数据接口与基类

### 1.1 `ISeedData<TEntity>`

所有种子数据类都必须实现此接口：

```csharp
public interface ISeedData<TEntity>
    where TEntity : class, new()
{
    /// <summary>
    /// 返回要作为种子插入的实体列表
    /// </summary>
    List<TEntity> GetSeedData();

    /// <summary>
    /// 执行种子数据插入逻辑
    /// </summary>
    void Execute(ISqlSugarClient db);
}
```

- `GetSeedData()`：提供待插入的数据。如果表已有数据（通过 `Any()` 判断），这些数据不会被重复插入。
- `Execute(ISqlSugarClient db)`：定义具体的插入行为，默认实现会调用 `GetSeedData` 并在表为空时执行批量插入。

### 1.2 `BaseSeedData<TEntity>`

抽象基类，提供了 `Execute` 的默认实现：

``` csharp
public abstract class BaseSeedData<TEntity> : ISeedData<TEntity>
    where TEntity : class, new()
{
    public virtual void Execute(ISqlSugarClient db)
    {
        var data = GetSeedData();
        var tableAny = db.AsTenant().QueryableWithAttr<TEntity>().Any();
        if (data != null && data.Any() && !tableAny)
        {
            db.AsTenant().InsertableWithAttr(data).ExecuteCommand();
        }
    }

    public abstract List<TEntity> GetSeedData();
}
```

- 使用 `AsTenant()` 以确保在多租户环境下种子数据写入正确的数据库连接。
- 使用 `QueryableWithAttr` 和 `InsertableWithAttr` 方法，这些方法是 SqlSugar 提供的扩展方法，能正确处理动态表名、拉姆达表达式等。

## 2. 创建种子数据类

### 2.1 基本步骤

创建一个类继承 `BaseSeedData<TEntity>`，并实现 `GetSeedData()` 方法：

``` csharp
public class UserSeedData : BaseSeedData<User>
{
    public override List<User> GetSeedData()
    {
        return new List<User>
        {
            new User { Name = "系统管理员", Email = "admin@example.com" },
            new User { Name = "普通用户", Email = "user@example.com" }
        };
    }
}
```

- `User` 必须实现 `IEntity`（通常继承 `BaseFullEntity`）。
- `Id`、`Creator`、`CreationTime` 等审计字段不需要手动设置，它们会在插入时通过 AOP 自动填充（参见 `sqlsugar-entity-audit.md`）。

### 2.2 支持多实体

每个种子数据类只负责一个实体类型。要为多个表添加种子数据，请为每个实体创建独立的种子数据类：

``` csharp
public class RoleSeedData : BaseSeedData<Role>
{
    public override List<Role> GetSeedData()
    {
        return new List<Role>
        {
            new Role { Name = "管理员" },
            new Role { Name = "用户" }
        };
    }
}
```

## 3. 执行机制

### 3.1 自动发现与执行

在 `AddLzqSqlSugar` 方法中，`UseSeedData()` 会扫描当前 Masa 程序集中所有实现了 `ISeedData<>` 的非抽象类，并依次执行它们的 `Execute` 方法。

``` csharp
public static ISqlSugarClient UseSeedData(this ISqlSugarClient db)
{
    var loadedAssemblies = MasaApp.GetAssemblies().ToList();
    var seedDataTypes = loadedAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } &&
                   t.GetInterfaces().Any(i => i.IsGenericType &&
                                              i.GetGenericTypeDefinition() == typeof(ISeedData<>)));

    foreach (var type in seedDataTypes)
    {
        try
        {
            var seedDataInstance = Activator.CreateInstance(type);
            var executeMethod = type.GetMethod("Execute",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            executeMethod?.Invoke(seedDataInstance, new object[] { db });
        }
        catch (Exception ex)
        {
            // 记录错误，不影响其他种子数据执行
        }
    }
    return db;
}
```

**关键点**：

- 使用 `Activator.CreateInstance` 创建实例，因此**种子数据类必须有无参构造函数**。
- 执行是顺序同步的，如果种子数据量大，应用启动会等待所有种子数据插入完成。
- 每个类内部的 `Execute` 方法会判断表是否为空，**空表才插入**，因此多次重启不会导致数据重复。但如果表已有部分数据（非完全空），种子数据不会被插入或更新。如需强制同步，需要自定义 `Execute` 实现。

### 3.2 执行顺序

种子数据的执行顺序取决于从程序集中反射类型的顺序，**不具备确定性**。如果种子数据之间存在依赖关系（例如必须先生成角色，再生成用户），请通过以下方式处理：

- 重写 `Execute` 方法，在一个种子数据类中手动调用其他种子数据的逻辑。
- 或从外部控制顺序（不推荐依赖反射顺序）。

### 3.3 与 CodeFirst 的顺序

在 `SqlSugarExtensions.AddLzqSqlSugar` 中，`UseSeedData` 是在 `UseCodeFirst` **之后**调用的，因此种子数据执行时，表结构已经存在，插入操作不会因表不存在而失败。

## 4. 自定义 Execute 行为

如果需要比“表为空则插入”更复杂的种子数据逻辑，可以覆盖 `Execute` 方法：

``` csharp
public class ConfigSeedData : BaseSeedData<AppConfig>
{
    public override List<AppConfig> GetSeedData()
    {
        return new List<AppConfig>
        {
            new AppConfig { Key = "SiteName", Value = "MyApp" }
        };
    }

    public override void Execute(ISqlSugarClient db)
    {
        var data = GetSeedData();
        foreach (var item in data)
        {
            var exists = db.AsTenant().Queryable<AppConfig>().Any(c => c.Key == item.Key);
            if (!exists)
            {
                db.AsTenant().Insertable(item).ExecuteCommand();
            }
        }
    }
}
```

这样可以实现按条件插入或更新种子数据。

## 5. 多数据库支持

- `Execute` 方法的默认实现中使用了 `db.AsTenant()`，这意味着种子数据会在**所有配置的数据库**的对应表中尝试插入（具体取决于 `AsTenant()` 的默认行为：如果当前没有明确的租户切换，它会遍历所有连接并执行相同的操作）。
- 如果某些种子数据只应存在于特定数据库，请重写 `Execute`，通过 `db.AsTenant().GetConnection("Tag")` 获取特定连接进行操作。

## 6. 注意事项

- **无参构造函数**：种子数据类必须包含公开的无参构造函数，否则 `Activator.CreateInstance` 会失败。
- **依赖注入**：当前设计不支持在种子数据类中注入服务（如 `ICurrentUser`）。若需要从 DI 获取服务，可在 `Execute` 中通过 `MasaApp.GetService<T>()` 获取，但需谨慎处理容器生命周期。
- **启动性能**：种子数据插入是同步阻塞操作，数据量大时会影响应用启动速度。建议为数据量大的场景采用数据库迁移脚本或异步任务。
- **异常处理**：单个种子数据类的异常会被捕获并记录日志，不会中断其他种子数据的执行，也不会阻止应用启动。
- **与 CodeFirst 的协同**：如果实体对应的表尚未创建（例如 CodeFirst 执行失败），种子数据执行时可能因表不存在而报错，请确保日志中有相关错误信息以便排查。

## 7. 常见问题

### 7.1 种子数据没有插入

- 检查表是否已经存在数据（`Any()` 判断），若已有任何数据，种子数据不会插入。
- 确认种子数据类实现了 `ISeedData<T>` 且是非抽象类，并且 `T` 是实体类型。
- 查看启动日志，是否有 `"种子数据 {type.FullName} 执行成功"` 或错误信息。

### 7.2 种子数据重复插入

- 默认实现只在表为空时插入，不会重复。如果自行重写 `Execute`，需注意幂等性。
- 多实例部署时，如果多个实例同时启动，且表都为空，可能同时插入相同数据。建议在种子数据中通过唯一约束或条件判断避免重复。

### 7.3 如何在不重启应用的情况下重新执行种子数据

- 种子数据仅在启动时执行一次。如需更新，建议通过管理接口或数据库迁移脚本操作。

### 7.4 种子数据类中的审计字段需要手动设置吗？

不需要。审计字段（`Creator`、`CreationTime` 等）会在插入时由 AOP 自动填充，`GetSeedData()` 中可忽略这些字段。