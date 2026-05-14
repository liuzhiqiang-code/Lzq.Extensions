---
name: lzq-development-quality
description: Lzq 框架开发规范与 AI 辅助审查技能。定义 .NET 项目命名规范、分层架构、API 设计、异常处理、日志、事务、验证、映射等标准。AI Agent 完成模块开发后，自动以资深 .NET 架构师身份审查代码，输出问题报告到 `problems/` 文件夹；编译报错时追溯相关扩展库或 skill 问题，为框架优化提供依据。适用于团队协作、持续改进框架及扩展库质量。
license: Proprietary
compatibility: .NET 6+ / .NET 10+、Lzq.Extensions 全家桶、MediatR、SqlSugar、Mapster、FluentValidation
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

本技能为使用 `Lzq.Extensions` 系列扩展库开发业务模块提供**强制规范**，并建立 **AI 自动审查与反馈闭环**。当 AI Agent 完成模块开发后，必须执行一次架构审查，生成问题报告；若编译失败，需分析根因并记录改进建议。框架维护者可依据这些材料持续优化扩展库及对应技能文档。

## 何时使用

- AI Agent 完成了新模块（如 Rbac、订单、AI 技能）的代码生成。
- 开发团队执行代码审查前，先让 AI 模拟资深架构师进行预审。
- 编译或运行时遇到与 Lzq 扩展库相关的错误，需要定位是使用问题还是库本身缺陷。
- 框架开发者需要收集反馈以改进 `Lzq.Extensions` 源码或技能文档。

## 核心规范

### 1. 命名规范

| 元素                     | 规范                                                         | 示例                                        |
| ------------------------ | ------------------------------------------------------------ | ------------------------------------------- |
| 项目名称                 | `Lzq.{Module}.{Layer}`<br>Layer: `Domain`, `Application.Contracts`, `Application`, `Host` | `Lzq.Rbac.Domain`, `Lzq.Order.Application`  |
| 实体类                   | `{Entity}Entity`                                             | `UserEntity`, `DeptEntity`                  |
| DTO                      | `{Entity}{Action}Dto` 或 `{Entity}ViewDto`                   | `DeptViewDto`, `UserCreateDto`              |
| Command/Query (record)   | 动词 + 名词 + `Command`/`Query`                              | `CreateUserCommand`, `GetUserQuery`         |
| 服务接口                 | `I{Module}Service`                                           | `IUserService`, `IDeptService`              |
| 服务实现                 | `{Module}Service`                                            | `UserService`, `DeptService`                |
| 仓储接口                 | `I{Entity}Repository`                                        | `IUserRepository`                           |
| 仓储实现                 | `{Entity}Repository`                                         | `UserRepository`                            |
| 枚举类型                 | Pascal 有意义名称，不强制后缀                                | `EnableStatusEnum`, `UserType`              |
| 异步方法                 | 返回 `Task` 的方法名以 `Async` 结尾                          | `GetUserAsync`, `SaveAsync`                 |
| 私有字段（不常用，但可） | `_camelCase`                                                 | `_logger`, `_repository`                    |

### 2. 分层与依赖方向

WebApi (启动项目)
↓ 引用
Application (服务实现)
↓ 引用
Application.Contracts (接口、DTO、Command/Query)
↓ 引用
Domain (实体、仓储接口、枚举)

``` text

- **禁止** Contracts 层引用 Application 层。
- **禁止** Domain 层引用除基础库以外的任何上层。
- **允许** Application 层引用所有下层。

### 3. API 设计规范

| 规范项                       | 要求                                                         |
| ---------------------------- | ------------------------------------------------------------ |
| 返回类型                     | 统一使用 `ApiResult` (无数据) 或 `ApiResult<T>` (有数据)     |
| 分页请求                     | 继承 `PagedRequest`                                          |
| 分页响应                     | 使用 `PagedResponse<T>`                                      |
| 基础路由                     | `ServiceBase` 构造参数传入，格式 `/api/v{version}/{module}/{entity}` |
| HTTP 方法映射                | 使用 `[RoutePattern]` 特性，显式指定 `HttpMethod`            |
| 参数绑定（支持远程调用时）   | 复杂参数 `[FromBody]`，路径参数 `[FromRoute]`，查询参数 `[FromQuery]` |
| Swagger 文档                 | 每个方法添加 `[OpenApiTag]` 和 `[OpenApiOperation]`          |

### 4. 异常处理规范

- **业务异常**：抛出 `UserFriendlyException`，框架全局中间件会捕获并返回 `ApiResult` 错误。
- **系统异常**：不必捕获，由 `UseCoreExceptionHandler` 统一处理并记录日志。
- **事务中异常**：会自动回滚，无需手动 `Rollback`。

### 5. 日志规范

- 使用 `ILogger<T>`，通过 `GetRequiredService<ILogger<T>>()` 或构造函数注入。
- 日志级别：`Debug` 详细信息（开发），`Information` 关键操作，`Warning` 降级/重试，`Error` 异常。
- 结构化日志：`logger.LogInformation("用户 {UserId} 创建订单 {OrderId}", userId, orderId);`

### 6. 事务处理规范

- 在 Command 上标记 `[UnitOfWork]` 特性（定义在 Contracts 层）。
- 不要手动 `BeginTran` / `CommitTran`，框架自动处理。
- 查询（`IQuery<T>`）不会触发事务。

### 7. 验证规范

- 每个 Command/Query 应编写一个 Validator 类。
- 继承 `MasaAbstractValidator<T>` 或 `AbstractValidator<T>`。
- 使用 FluentValidation 规则，错误消息清晰。
- 验证器会自动被 MediatR 管道调用，不需要手动触发。

### 8. 对象映射规范

- 使用 Mapster（全局配置后自动处理）。
- 映射方法：`source.Map<TDestination>()`。
- 复杂映射可在 `MapsterConfig` 中全局配置。

## AI 审查流程

当 AI Agent 完成一个模块的代码生成后，**必须执行以下步骤**：

### 步骤 1：自检编译

- 模拟编译（或等待真实编译结果）。
- 如果编译失败，跳转到 **错误诊断流程**。

### 步骤 2：静态规范审查

按照上述规范检查生成的代码。重点检查：

- ✅ 项目依赖是否正确（Contracts 不引用 Application 等）。
- ✅ 实体是否继承 `BaseFullEntity`，表名和列名特性是否正确。
- ✅ 仓储是否实现 `ISqlSugarRepository<T>` 并标记 `ITransientDependency`。
- ✅ Command/Query 是否使用 `record` 类型，是否包含必要属性。
- ✅ Validator 是否与 Command/Query 一一对应，规则是否完整。
- ✅ 服务类是否继承 `ServiceBase`，是否传入正确路由。
- ✅ 每个方法是否有 `[OpenApiTag]`、`[OpenApiOperation]`、`[RoutePattern]`。
- ✅ 返回类型是否统一使用 `ApiResult`。
- ✅ 是否使用 `Map<T>` 进行对象转换。
- ✅ 是否有不必要的 `[FromBody]` 在本地仅调用的接口上（建议不加）。
- ✅ 异步方法命名是否以 `Async` 结尾。

### 步骤 3：生成问题报告

将审查发现的问题写入 `workspace/problems/{ModuleName}-review-{timestamp}.md`。

**报告格式**：

‍```markdown
# 代码审查报告 - {模块名称}

**审查时间**：{timestamp}
**模块路径**：{相对路径}

## 概览
- 总文件数：{n}
- 问题总数：{m}
- 严重级别：🔴 阻塞 | 🟠 重要 | 🟡 建议

## 问题列表

### {文件路径}

#### 🔴 {问题标题}
- **行号**：{L}
- **描述**：{具体违反的规范}
- **建议修复**：{如何修改}
- **参考**：{相关技能文档章节}

（重复）

## 总结建议
{整体改进意见}
```

### 步骤 4：编译错误诊断（如果发生）

若编译失败，AI 必须：

1. **识别错误类型**：

    - 缺少包引用 → 检查 `.csproj` 及 `Directory.Build.props`
    - 类型不存在 → 检查 using 语句或是否遗漏项目引用
    - 方法签名不匹配 → 检查接口与实现是否一致
    - 命名空间错误 → 检查推荐 using（如 `Lzq.Core.Attributes`、`NSwag.Annotations`）
2. **追溯责任 skill**：

    - 判断错误是否因为误解了某个扩展库的用法（例如错误使用 `[RoutePattern]` 而该特性来自 `Lzq.Core` 但未 `using`）。
    - 记录是哪个 skill 的描述不清晰或示例有误。
3. **生成诊断报告** 到 `workspace/problems/compile-error-{timestamp}.md`：

``` markdown
# 编译错误诊断报告

**时间**：{timestamp}
**模块**：{模块名}
**编译命令**：dotnet build

## 错误摘要
- 错误代码：CS{xxx}
- 错误消息：{原文}

## 故障分析
{分析可能的根本原因}

## 关联 Skill
- **Skill 名称**：{如 lzq-extensions-sqlsugar}
- **Skill 问题**：{缺少某个关键步骤，或示例代码与当前版本不兼容}
- **改进建议**：{如何修改 skill 文档或扩展库代码}

## 解决此错误的临时方案
{针对当前项目的修改步骤}
```

## 反馈闭环

框架开发者应定期收集 `problems/` 目录下的所有报告，并：

- 修正扩展库源码中的缺陷。
- 更新对应的 skill 文档（补充遗漏点，修正示例）。
- 发布新版本 `Lzq.Extensions` 并更新 `Directory.Build.props` 中的版本号。

## 示例：审查问题片段

``` markdown
### src/Lzq.Order.Application/Services/OrderService.cs

#### 🟠 缺少 Swagger 文档特性
- **行号**：23
- **描述**：方法 `CreateAsync` 未添加 `[OpenApiOperation]` 特性。
- **建议修复**：添加 `[OpenApiOperation("创建订单", "创建新订单并触发库存扣减")]`
- **参考**：`lzq-module-development` → 服务实现规范
```

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
- lzq-agentforge-webapi
- lzq-module-development