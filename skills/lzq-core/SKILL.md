---
name: lzq-core
description: 基于 Lzq.Core 基础 NuGet 包开发业务模块。在构建新的 CRUD 服务、API 端点、领域实体或与 Lzq 平台集成时使用。涵盖 ServiceBase 模式、ApiResult 响应、Masa MinimalAPIs、Mapster 映射、FluentValidation 验证、UnitOfWork 事务以及完整的请求生命周期。
license: Proprietary
compatibility: 需要 .NET 8+、Lzq.Core NuGet 包、Masa Framework
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

本技能提供了在 `Lzq.Core` 基础上开发业务模块的标准化模式。每个新的业务功能（例如“工单管理”、“库存跟踪”）都遵循相同的分层架构：

HTTP 请求 → ServiceBase (MinimalAPI) → IRepository → 数据库
↓
ApiResult<T> (统一响应格式)

ApiResult<T> (统一响应格式)

## 何时使用

- 用户要求“为 X 创建一个新的 API”或“添加一个新的业务模块”
- 希望实现带有分页、过滤、事务的 CRUD 操作
- 需要将新实体集成到现有的 Lzq 平台中
- 询问 ApiResult、ServiceBase、Mapster 映射、MinimalAPI 模式
- 在开发上下文中提到 `Lzq.Core`、`Masa` 或 `AgentForge`

## 何时不使用

- 纯前端任务（改用 frontend-design 或 web-artifacts-builder 技能）
- 修改现有 NuGet 包的内部代码
- 非 .NET 的项目

## 架构层次

| 层次 | 基类 / 接口 | 职责 |
|---|---|---|
| **Service (API)** | `ServiceBase` | 定义端点，验证输入，返回 `ApiResult` |
| **Repository** | `IRepository<T>` (SqlSugar) | 数据访问、查询 |
| **Entity** | `BaseFullEntity` | 数据库表映射 |
| **DTO** | `record` / `class` | 请求/响应契约 |
| **Mapping** | `.Map<T>()` (Mapster) | 实体 ↔ DTO 转换 |

## 分步指南：构建一个新的业务模块

### 第 1 步：创建实体

```csharp
[Tenant("AgentForge"), SugarTable("ai_xxx")]
public class XxxEntity : BaseFullEntity
{
    [SugarColumn(ColumnName = "name", Length = 100)]
    public string Name { get; set; }
    
    [SugarColumn(ColumnName = "status")]
    public EnableStatusEnum Status { get; set; } = EnableStatusEnum.Enabled;
}
```
关键规则：

- 继承 BaseFullEntity（提供 Id、Creator、CreationTime 等字段）

- 使用 [Tenant("AgentForge")] 和 [SugarTable] 特性

- 所有属性都应添加 [SugarColumn]，字符串需指定 Length

- 需要软删除的实体实现 IDeleted 接口

### 第 2 步：创建 DTO（请求 / 响应 / 视图）

``` csharp
// 位于 Application.Contracts 命名空间
public record XxxCreateCommand(string Name);
public record XxxUpdateCommand(long Id, string Name);
public record XxxViewDto(long Id, string Name, EnableStatusEnum Status);
public record XxxPageRequest : PagedRequest
{
    public string? Keyword { get; set; }
}
```

### 第 3 步：创建 Service

```csharp
public class XxxService : ServiceBase
{
    public XxxService() : base("/api/v1/xxx") { }
    
    private IRepository<XxxEntity> Repo => GetRequiredService<IRepository<XxxEntity>>();
    private ILogger<XxxService> Logger => GetRequiredService<ILogger<XxxService>>();

    [OpenApiTag("xxx"), RoutePattern(pattern: "page", true)]
    public async Task<ApiResult> PageAsync([FromBody] XxxPageRequest request)
    {
        var query = Repo.AsQueryable();
        if (!string.IsNullOrEmpty(request.Keyword))
            query = query.Where(x => x.Name.Contains(request.Keyword));
        
        var total = await query.CountAsync();
        var items = await query.Skip((request.Page - 1) * request.PageSize)
                               .Take(request.PageSize).ToListAsync();
        
        return ApiResult.Success(new PagedResponse<XxxViewDto>(
            items.Map<List<XxxViewDto>>(), total));
    }

    [OpenApiTag("xxx"), RoutePattern(pattern: "create", true)]
    public async Task<ApiResult> CreateAsync([FromBody] XxxCreateCommand cmd)
    {
        var entity = cmd.Map<XxxEntity>();
        await Repo.InsertAsync(entity);
        return ApiResult.Success();
    }

    [OpenApiTag("xxx"), RoutePattern(pattern: "update", true)]
    public async Task<ApiResult> UpdateAsync([FromBody] XxxUpdateCommand cmd)
    {
        var entity = await Repo.GetByIdAsync(cmd.Id)
            ?? throw new UserFriendlyException("记录不存在");
        cmd.Map(entity);
        await Repo.UpdateAsync(entity);
        return ApiResult.Success();
    }

    [OpenApiTag("xxx"), RoutePattern(pattern: "delete/{id}", true)]
    public async Task<ApiResult> DeleteAsync(long id)
    {
        await Repo.DeleteAsync(x => x.Id == id);
        return ApiResult.Success();
    }
}
```
关键规则：

- 始终继承 ServiceBase 并在构造函数中传入 API 路由前缀

- 使用 GetRequiredService<T>() 解析依赖（属性注入）

- 所有端点返回 ApiResult 或 ApiResult<T>

- 业务错误使用 UserFriendlyException（不要直接使用 Exception）

- 使用 .Map<T>() 进行实体与 DTO 的转换

- 使用 [OpenApiTag] 和 [RoutePattern] 自动注册 MinimalAPI

### 第 4 步：注册依赖
在模块的 AddXxxModule 扩展方法中：

``` csharp
public static IServiceCollection AddXxxModule(this IServiceCollection services)
{
    services.AddCoreAssembly("Lzq.Xxx.");      // 自动发现程序集
    services.AddCoreAutoInject();                // 自动注册 DI
    return services;
}
```

### 第 5 步：在 Program.cs 中注册
``` csharp
builder.Services.AddCoreAssembly("Lzq.").AddMapster().AddCoreAutoInject();
builder.Services.AddCoreMinimalAPIs();
// ...
app.MapMasaMinimalAPIs();
```

## 关键模式参考
### ApiResult 用法
场景	方法
成功，无数据	`ApiResult.Success()`
成功，带数据	`ApiResult.Success(data)`
业务错误	`throw new UserFriendlyException("消息")`
验证错误	`throw new MasaValidatorException(...)`
未找到	`return ApiResult.Fail("未找到", 404)`

### 事务支持
``` csharp
[UnitOfWork(isolationLevel: IsolationLevel.ReadCommitted)]
public async Task<ApiResult> CreateWithTransactionAsync([FromBody] XxxCommand cmd)
{
    await Repo1.InsertAsync(entity1);
    await Repo2.InsertAsync(entity2);
    return ApiResult.Success();
}
```

### 当前用户
``` csharp
private ICurrentUser CurrentUser => GetRequiredService<ICurrentUser>();
// CurrentUser.UserId, CurrentUser.UserName, CurrentUser.Roles
```

## 参考资料
详细代码模板请参阅：

- references/service-pattern.md — 完整的 ServiceBase 实现模板

- references/entity-pattern.md — 实体设计及 SqlSugar 配置

- references/crud-endpoint-pattern.md — 所有 CRUD 端点快速参考

- references/dependency-pattern.md — 模块注册与 DI 约定