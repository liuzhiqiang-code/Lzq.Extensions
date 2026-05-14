# 实体设计及 SqlSugar 配置

## BaseFullEntity 基类

`BaseFullEntity` 是所有业务实体的基类，提供以下字段（由框架自动维护）：

| 字段 | 类型 | 说明 |
|---|---|---|
| `Id` | `long` | 主键，自增 |
| `Creator` | `long?` | 创建者 |
| `CreationTime` | `DateTime` | 创建时间 |
| `Modifier` | `long?` | 修改者 |
| `ModificationTime` | `DateTime` | 修改时间 |
| `IsDeleted` | `bool` | 软删除标记 |

继承该基类无需重复定义以上字段，同时可获得框架提供的自动赋值、软删除等功能。

## 必备 Attributes

每个实体必须包含以下特性：

``` csharp
[Tenant("AgentForge")]       // 多租户标识（固定）
[SugarTable("rbac_xxx")]    // 数据库表名
```

## SugarColumn 配置项

| 配置 | 示例 | 说明                          |
| ------ | ------ | ------------------------------- |
| `ColumnName`     | `"user_name"`     | 列名映射                      |
| `Length`     | `100`     | 字符串长度                    |
| `IsNullable`     | `true`     | 可空                          |
| `IsJson`     | `true`     | JSON 列（用于复杂类型，如 `MenuMeta`）  |
| `ColumnDataType`     | `"text"`     | 列数据类型                    |
| `IsIgnore`     | `true`     | 忽略映射（用于内存属性，如 `Children`） |
| `IsPrimaryKey`     | `true`     | 主键（通常由基类提供）        |
| `IsIdentity`     | `true`     | 自增（通常由基类提供）        |

## 完整实体示例

``` csharp
[Tenant("AgentForge")]
[SugarTable("ai_work_order")]
public class WorkOrderEntity : BaseFullEntity, IDeleted // 实现 IDeleted 启用软删除
{
    [SugarColumn(ColumnName = "code", Length = 50)]
    public string Code { get; set; }

    [SugarColumn(ColumnName = "title", Length = 200)]
    public string Title { get; set; }

    [SugarColumn(ColumnName = "description", Length = 500, IsNullable = true)]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "status")]
    public EnableStatusEnum Status { get; set; } = EnableStatusEnum.Enabled;

    [SugarColumn(ColumnName = "assigned_to", IsNullable = true)]
    public long? AssignedTo { get; set; }

    [SugarColumn(ColumnName = "completed_time", IsNullable = true)]
    public DateTime? CompletedTime { get; set; }
}
```

## 枚举映射

枚举属性直接存储为整数或字符串，框架自动转换。

``` csharp
[SugarColumn(ColumnName = "status")]
public EnableStatusEnum Status { get; set; } = EnableStatusEnum.Enabled;
```

## 软删除

实现 `IDeleted` 接口后，调用 `Repository.DeleteAsync()` 或 `Deleteable().Where(...).ExecuteCommandAsync()` 会自动执行软删除（将 `IsDeleted` 设为 `true`），而非物理删除。

``` csharp
public class XxxEntity : BaseFullEntity, IDeleted
{
    // ...
}
```

## 索引

如需唯一索引或复合索引，可在实体上使用 `SugarIndex` 特性：

``` csharp
[SugarIndex("IX_Code", nameof(Code), OrderByType.Asc, true)] // 唯一索引
public class WorkOrderEntity : BaseFullEntity
{
    // ...
}
```

## 注意事项

- 继承 `BaseFullEntity` 后，不要再手动定义 `Id`、`CreationTime` 等字段，避免冲突。
- JSON 列（如 `Meta`）序列化时使用 `System.Text.Json`，属性名默认驼峰，可通过 `[JsonPropertyName]` 自定义。
- 所有字符串列必须显式指定 `Length`，否则可能导致数据库列类型不准确。
- 实体类型必须与数据库表 `[SugarTable]` 中的名称完全一致。