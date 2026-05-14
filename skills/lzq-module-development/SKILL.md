---
name: lzq-module-development
description: 基于 Lzq.Extensions 全家桶（SqlSugar、EventBus、NSwag、ExternalHttpApi、JWT 等）开发企业级业务模块的参考指南。涵盖模块分层结构（Domain / Application.Contracts / Application）、动态 API 实现、仓储模式、自动验证、种子数据和依赖注入。适合需要创建类似 Rbac 模块（如部门管理、用户管理、权限管理）的场景。
license: Proprietary
compatibility: .NET 6+ / .NET 10+、Lzq.Extensions 0.1.33+、SqlSugar、MediatR
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Rbac` 模块是一个标准的业务模块实现，展示了如何使用 Lzq.Extensions 扩展库构建一个功能完整的 CURD + 树形结构模块（部门管理）。该模块包含以下三层层级：

- **Domain**：实体、枚举、仓储接口、仓储实现、种子数据。
- **Application.Contracts**：DTO、消息契约（Command/Query）、服务接口、验证器。
- **Application**：服务实现（基于 `ServiceBase` + `RoutePattern`）、对象映射、业务逻辑。

使用该 skill 作为模板，可以快速搭建类似的结构（如角色管理、菜单管理、用户管理等）。

## 何时使用

- 需要创建一个新的业务模块，且项目已集成了 Lzq.Extensions 全家桶。
- 希望复用 Rbac 模块的设计模式：动态 API、自动 Swagger 文档、SqlSugar 仓储、自动验证、种子数据。
- 需要支持本地调用（通过依赖注入）和远程调用（通过 `Lzq.Extensions.ExternalHttpApi`）的双模式。
- 需要构建递归树形结构（如部门树、菜单树）。

## 何时不使用

- 不使用 Lzq.Extensions 扩展库的项目。
- 项目使用其他 ORM（如 EF Core）或其他验证框架。
- 只需要简单的 CRUD，不需要分层或模块化。

## 模块结构模板

Lzq.YourModule/
├── Lzq.YourModule.Domain/ # 领域层
│ ├── Entities/ # 实体（继承 BaseFullEntity）
│ ├── Enums/ # 枚举
│ ├── IRepositories/ # 仓储接口（继承 ISqlSugarRepository\<T\>）
│ ├── Repositories/ # 仓储实现（继承 SqlSugarRepository\<T\>）
│ └── SeedDatas/ # 种子数据（继承 BaseSeedData\<T\>）
├── Lzq.YourModule.Application.Contracts/ # 契约层（供外部引用）
│ ├── YourEntity/ # 按模块功能分组
│ │ ├── Commands/ # 命令（Create, Update, Delete 等）
│ │ ├── Queries/ # 查询（Page, List, Get 等）
│ │ ├── Dto/ # 数据传输对象
│ │ └── Validators/ # FluentValidation 验证器
│ └── IServices/ # 服务接口
└── Lzq.YourModule.Application/ # 应用层（服务实现）
└── Services/ # 实现类（继承 ServiceBase）

text

```

## 分层详解

### 1. Domain 层

**责任**：定义数据实体、枚举、仓储抽象和种子数据。

**实体示例**（`DeptEntity`）：

‍```csharp
[Tenant("AgentForge")]               // 多数据库标签，与 appsettings.json 中的 DBConfigs 对应
[SugarTable("rbac_dept")]            // 表名
public class DeptEntity : BaseFullEntity   // 自带 Id, CreateTime, UpdateTime, IsDeleted 等
{
    [SugarColumn(ColumnName = "pid")]
    public long? Pid { get; set; }

    [SugarColumn(ColumnName = "name", Length = 100)]
    public string Name { get; set; }

    [SugarColumn(ColumnName = "status")]
    public EnableStatusEnum Status { get; set; }

    [SugarColumn(ColumnName = "remark", Length = 2000)]
    public string? Remark { get; set; }
}
```

**仓储接口与实现**：

csharp

```
// IRepositories/IDeptRepository.cs
public interface IDeptRepository : ISqlSugarRepository<DeptEntity>, ITransientDependency
{
}

// Repositories/DeptRepository.cs
public class DeptRepository() : SqlSugarRepository<DeptEntity>(), IDeptRepository
{
}
```

**种子数据**（可选，用于初始化数据）：

csharp

```
public class DeptSeedData : BaseSeedData<DeptEntity>
{
    public override List<DeptEntity> GetSeedData()
    {
        return new List<DeptEntity>
        {
            new DeptEntity { Id = 1, Pid = null, Name = "总公司", Status = EnableStatusEnum.Enabled }
        };
    }
}
```

> 种子数据会在 `AddLzqSqlSugar` 时自动执行（取决于配置）。

### 2. Application.Contracts 层

**责任**：定义外部可见的 DTO、命令/查询、验证规则、服务接口。此层不包含实现，仅定义契约。

**DTO 示例**（`DeptViewDto`）：

csharp

```
public class DeptViewDto
{
    public long Id { get; set; }
    public long? Pid { get; set; }
    public string Name { get; set; }
    public EnableStatusEnum Status { get; set; }
    public string? Remark { get; set; }
    public List<DeptViewDto> Children { get; set; }  // 树形结构用
}
```

**Command 示例**（`DeptCreateCommand`）：

csharp

```
public record DeptCreateCommand
{
    public long? Pid { get; set; }
    public string Name { get; set; }
    public EnableStatusEnum Status { get; set; } = EnableStatusEnum.Enabled;
    public string? Remark { get; set; }
}
```

**Validator 示例**（继承 `MasaAbstractValidator<T>`）：

csharp

```
public class DeptCreateCommandValidator : MasaAbstractValidator<DeptCreateCommand>
{
    public DeptCreateCommandValidator()
    {
        RuleFor(a => a.Name).NotNull().NotEmpty().WithMessage("部门名称不能为空");
        WhenNotEmpty(a => a.Remark, 
            rule => rule.Length(0, 500).WithMessage("备注信息不能超过500字符"));
    }
}
```

**Query 示例**（`DeptPageQuery` 继承 `PagedRequest`）：

csharp

```
public record DeptPageQuery : PagedRequest
{
    public long? Id { get; set; }
    public long? Pid { get; set; }
    public string? Name { get; set; }
    public int? Status { get; set; }
    public string? Remark { get; set; }
}
```

**服务接口**（`IDeptService`）：

csharp

```
public interface IDeptService
{
    Task<ApiResult> PageAsync(DeptPageQuery query);
    Task<ApiResult> ListAsync(DeptListQuery query);
    Task<ApiResult> CreateAsync(DeptCreateCommand command);
    Task<ApiResult> UpdateAsync(DeptUpdateCommand command);
    Task<ApiResult> DeleteAsync(long id);
    Task<ApiResult> BatchDeleteAsync(List<long> ids);
}
```

> **重要**：服务接口中**不要添加** `[FromBody]` 等特性，除非明确需要支持远程调用（`Lzq.Extensions.ExternalHttpApi`）。本地调用不需要。

### 3. Application 层

**责任**：实现服务接口，处理业务逻辑、对象映射、仓储调用。

**服务实现基类**：继承 `ServiceBase` 并提供基础路径。

csharp

```
public class DeptService : ServiceBase, IDeptService
{
    public DeptService() : base("/api/v1/rbac/dept") { }  // 定义 API 基础路由

    private IDeptRepository DeptRepository => GetRequiredService<IDeptRepository>();
}
```

**实现方法**（以 PageAsync 为例）：

csharp

```
[OpenApiTag("rbac/dept"), OpenApiOperation("获取部门分页列表", "")]
[RoutePattern(pattern: "page", true)]
public async Task<ApiResult> PageAsync([FromBody] DeptPageQuery query)
{
    RefAsync<int> total = 0;
    var pageList = await DeptRepository.AsQueryable().ToPageListAsync(query.Page, query.PageSize, total);
    var result = pageList.Map<List<DeptViewDto>>();
    return ApiResult.Success(new PagedResponse<DeptViewDto>(result, total));
}
```

**关键特性说明**：

| 特性 | 作用                                                               | 来源命名空间 |
| ------ | -------------------------------------------------------------------- | -------------- |
| `[OpenApiTag]`     | 为 Swagger 分组，对应 NSwag 文档中的标签                           | `NSwag.Annotations`             |
| `[OpenApiOperation]`     | 提供操作摘要和描述                                                 | `NSwag.Annotations`             |
| `[RoutePattern]`     | 声明此方法的 HTTP 方法、路由模板及是否启用（第三个参数为启用标志） | `Lzq.Core.Attributes`（推测）     |
| `[FromBody]`     | 仅当方法需要作为 HTTP 端点被调用时使用（本地调用可省略）           | `Microsoft.AspNetCore.Mvc`             |

**对象映射**：使用 Mapster（通过 `Map<T>` 扩展方法）。

**树形结构构建**（示例）：

csharp

```
private List<DeptViewDto> BuildDeptTree(List<DeptViewDto> allDepts, long? parentId)
{
    return allDepts
        .Where(d => d.Pid == parentId)
        .Select(d => new DeptViewDto
        {
            Id = d.Id,
            Pid = d.Pid,
            Name = d.Name,
            Status = d.Status,
            Remark = d.Remark,
            Children = BuildDeptTree(allDepts, d.Id)
        })
        .ToList();
}
```

## 动态 API 注册原理

`Lzq.Core` 提供了 `AddCoreMinimalAPIs()` 扩展方法，它会扫描所有继承自 `ServiceBase` 的服务类，并自动将其公开为 Minimal API 端点，无需手动编写 Controller。

要求：

- 服务类必须继承 `ServiceBase`，并在构造函数中传入基础路由（如 `/api/v1/rbac/dept`）。
- 每个公共方法需要标记 `[RoutePattern]`，指定相对于基础路由的路径和 HTTP 方法（如 `pattern: "page", true` 表示 POST `{base}/page`）。
- 方法的参数会按照 [ASP.NET](https://asp.net/) Core 模型绑定规则进行解析（如果需要支持 HTTP 调用，参数上可以添加 `[FromBody]`、`[FromRoute]` 等；如果仅本地调用，可以不加）。

**如果同时需要本地调用和 HTTP 调用**：接口中的参数**必须**加上模型绑定特性（如 `[FromBody]`），Local 调用时这些特性会被忽略。

## 自动依赖注入

所有实现 `ITransientDependency`、`IScopedDependency`、`ISingletonDependency` 的接口/类，会被 `AddCoreAutoInject()` 自动注册。示例中：

- `IDeptRepository` 继承 `ITransientDependency` → 自动注册为 Transient。
- `DeptRepository` 会自动注册为其实现的接口。

无需手动 `services.AddScoped<IDeptRepository, DeptRepository>()`。

## 验证器自动注册

`AddEventBus()` 或 `AddCoreAutoInject()` 会扫描所有继承 `AbstractValidator<T>` 或 `MasaAbstractValidator<T>` 的类，并自动注册到 DI 容器。验证器在 MediatR 管道 `ValidatorBehavior` 中自动触发。

## 种子数据自动执行

当调用 `AddLzqSqlSugar` 时，会扫描继承 `BaseSeedData<T>` 的类，并按顺序执行 `GetSeedData()` 插入到数据库（如果表为空）。

## 模块项目文件配置

每个项目文件需设置正确的包引用和版本。

**Domain.csproj**（参考）：

xml

```
<ItemGroup>
  <PackageReference Include="Lzq.Extensions.SqlSugar" Version="$(LzqExtensionsVersion)" />
</ItemGroup>
```

**Application.Contracts.csproj**（参考）：

xml

```
<ItemGroup>
  <PackageReference Include="Lzq.Extensions.Jwt" Version="$(LzqExtensionsVersion)" />
  <PackageReference Include="Lzq.Extensions.ExternalHttpApi" Version="$(LzqExtensionsVersion)" />
</ItemGroup>
```

**Application.csproj**（参考）：

xml

```
<ItemGroup>
  <PackageReference Include="Lzq.Core" Version="$(LzqExtensionsVersion)" />
  <PackageReference Include="Lzq.Extensions.NSwag" Version="$(LzqExtensionsVersion)" />
</ItemGroup>
```

版本号在解决方案根目录的 `Directory.Build.props` 中统一管理：

xml

```
<PropertyGroup>
  <LzqExtensionsVersion>0.1.33</LzqExtensionsVersion>
</PropertyGroup>
```

## 调试 Lzq.Extensions 库的工作流

当需要修改 Lzq.Extensions 库代码并调试时，必须遵循以下标准流程：

### 修改 → 发布 → 验证流程

1. **修改 Lzq.Extensions 源代码**
   - 修改 `D:\gitee\Lzq.Extensions\src\` 下的相关项目

2. **打包发布所有库**
   ```bash
   cd D:\gitee\Lzq.Extensions
   ./build.bat
   ```
   - 输出位置：`D:\gitee\Lzq.Extensions\packages\*.nupkg`
   - 自动生成 net8.0 / net9.0 / net10.0 多目标框架包

3. **更新版本号**
   
   **Lzq.Extensions 版本**（每次发布必须累加）：
   ```
   文件: D:\gitee\Lzq.Extensions\Directory.Build.props
   修改: <Version>X.Y.Z</Version>
   ```
   
   **WorkBuddy 引用版本**（必须与 Lzq.Extensions 同步）：
   ```
   文件: D:\gitee\WorkBuddy\code\Directory.Build.props
   修改: <LzqExtensionsVersion>X.Y.Z</LzqExtensionsVersion>
   ```

4. **记录修改**
   ```
   文件: D:\gitee\WorkBuddy\problems\Lzq.Extensions修改记录_YYYYMMDD.md
   内容:
   - 修改的文件列表（含完整路径）
   - 修改内容说明
   - 版本变更记录
   - 发布步骤
   ```

### 重要规则

- **所有库必须一起打包**：build.bat 会打包所有 10 个 Lzq.Extensions 库
- **版本号必须单调递增**：不允许回退版本号
- **每次修改必须记录**：在 problems 目录创建修改记录文件
- **WorkBuddy 版本同步**：修改 Lzq.Extensions 后必须更新 WorkBuddy 的引用版本

### 当前版本状态

- Lzq.Extensions 版本：0.1.36
- WorkBuddy 引用版本：0.1.36

## 开发新模块的步骤

1. **创建三个项目**：`Lzq.YourModule.Domain`、`Lzq.YourModule.Application.Contracts`、`Lzq.YourModule.Application`。
2. **在 Domain 层**：

    - 定义实体（继承 `BaseFullEntity`，添加需要的属性）。
    - 定义枚举。
    - 定义仓储接口（继承 `ISqlSugarRepository<T>` 并标记 `ITransientDependency`）。
    - 实现仓储（继承 `SqlSugarRepository<T>`）。
    - （可选）添加种子数据。
3. **在 Contracts 层**：

    - 定义 DTO（对应实体视图）。
    - 定义 Command（Create, Update, Delete 等）和 Query（Page, List, Get 等）。
    - 定义每个 Command/Query 的验证器（继承 `MasaAbstractValidator<T>`）。
    - 定义服务接口（`IYourService`），方法返回 `Task<ApiResult>`。
4. **在 Application 层**：

    - 创建服务实现类，继承 `ServiceBase`，传入基础路由。
    - 实现接口方法，使用 `Map<T>()` 映射实体 ↔ DTO。
    - 使用 `GetRequiredService<T>()` 获取仓储或其他服务。
    - 添加 `[OpenApiTag]`、`[OpenApiOperation]`、`[RoutePattern]` 特性。
5. **在 WebApi 启动项目中**：

    - 确保模块的项目被引用。
    - `AddCoreAssembly()` 会自动扫描并加载模块。
    - `AddCoreMinimalAPIs()` 会注册所有 `ServiceBase` 派生类为动态 API。

## 最佳实践

- **命名约定**：`I{Module}Service` 接口，`{Module}Service` 实现。
- **返回类型**：始终使用 `ApiResult`（成功）或 `ApiResult<T>`（带数据）。错误通过抛出异常（如 `UserFriendlyException`）由 `UseCoreExceptionHandler` 处理。
- **分页请求**：继承 `PagedRequest`，响应使用 `PagedResponse<T>`。
- **树形结构**：DTO 中包含 `Children` 集合，服务层构建递归树。
- **事务**：如果 Command 需要事务，添加 `[UnitOfWork]` 特性到 Command 类上（在 Contracts 层定义）。
- **日志**：使用 `ILogger<T>` 记录关键操作（可通过 `GetRequiredService<ILogger<DeptService>>()` 获取）。

## 兼容远程调用

如果需要 `IYourService` 同时支持本地和远程调用（通过 `ExternalHttpApi`），请在 Contracts 层的接口参数上添加 `[FromBody]`、`[FromRoute]` 等特性。Application 层的实现类不需要修改，但需保留相同的特性以保持方法签名一致。

## 参考资料 
其他关联skills
- lzq-core
- lzq-extensions-eventbus
- lzq-extensions-eventbus-rabbitmq
- lzq-extensions-externalhttpapi
- lzq-extensions-jwt
- lzq-extensions-nswag
- lzq-extensions-redis
- lzq-extensions-serilog
- lzq-extensions-sqlsugar