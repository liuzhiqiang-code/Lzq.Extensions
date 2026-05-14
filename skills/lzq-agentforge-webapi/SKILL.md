---
name: lzq-agentforge-webapi
description: 基于 Lzq.Extensions 全家桶构建 AI Agent 级 Web API 的参考架构。集成 CQRS/事件总线 (RabbitMQ + Outbox)、SqlSugar 多库 ORM、Serilog 日志、JWT 认证、NSwag 文档、AI 技能 (Lzq.Extensions.AI)、健康检查、CORS 等。适用于需要快速搭建企业级 .NET Web API 且深度使用 Lzq 扩展库的 AI 应用场景。
license: Proprietary
compatibility: .NET 10+、Lzq.Extensions.* (0.1.33+)、MASA Framework (1.2.0-preview.10)
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.AgentForge.WebApi` 是一个全功能的 Web API 参考实现，展示了如何使用 `Lzq.Extensions` 系列扩展库（EventBus、SqlSugar、Serilog、JWT、NSwag、AI 等）来构建基于 CQRS、事件驱动、多数据库、AI 技能编排的现代 .NET 应用程序。该架构适用于 AI Agent 系统、微服务网关或中台服务。

在以下场景参考此 skill：
- 需要搭建使用 `Lzq.Extensions` 全家桶的 .NET Web API 项目。
- 需要集成 CQRS + 事件总线 (RabbitMQ) + Outbox 模式。
- 需要集成 AI 技能（AgentSkills）和聊天历史存储（SqlSugar）。
- 需要多数据库支持（SqlSugar + 多租户标签）。
- 需要规范化配置健康检查、JWT、NSwag 文档、序列化转换等。

## 何时使用

- 新项目启动，希望复用 Lzq 扩展库的最佳实践。
- 现有项目需要添加事件总线、AI 能力或多数据库。
- 需要参考如何整合 `Lzq.Extensions.*` 命名空间下的所有组件。
- 想要一个开箱即用的 Web API 模板代码（Program.cs、appsettings.json、健康检查、CORS 等）。

## 何时不使用

- 不使用 Lzq.Extensions 扩展库的项目，或只需单个组件。
- 使用其他框架（如 ABP vNext）或完全不同的技术栈。
- 不需要复杂集成（例如简单的 CRUD API 无需事件总线或 AI）。

## 参考架构概览

该 Web API 包含以下关键特性：

| 特性                     | 实现组件                                             | 说明                                           |
| ------------------------ | ---------------------------------------------------- | ---------------------------------------------- |
| 日志                     | `Lzq.Extensions.Serilog`                             | 文件日志、结构化日志                           |
| 数据库 ORM               | `Lzq.Extensions.SqlSugar`                            | 支持多数据库（Sqlite/MySQL 等），多库配置      |
| CQRS + 事件总线          | `Lzq.Extensions.EventBus` + RabbitMQ + Outbox        | 进程内事件 + 集成事件 + 可靠 Outbox            |
| AI 技能                  | `Lzq.Extensions.AI` + `AgentSkills`                  | 对话、工具调用、聊天历史存储                   |
| REST API 文档            | `Lzq.Extensions.NSwag`                               | Swagger UI + 密码保护                          |
| 认证授权                 | `Lzq.Extensions.Jwt`                                 | JWT Bearer 认证                                |
| 外部 HTTP API 调用       | `Lzq.Extensions.ExternalHttpApi`                     | 声明式 HttpClient 配置                         |
| 健康检查                 | 自定义 `MemoryHealthCheck` + ASP.NET Core Health     | 内存及服务存活检查                             |
| 跨域 CORS                | 内置 CORS                                            | 允许所有来源（可调整）                         |
| JSON 序列化              | 自定义 `LongToStringConverter`                       | 长整型序列化为字符串（避免 JS 精度丢失）       |
| 自动注入和 Mapster       | `Lzq.Core` 中的 `AddCoreAssembly` + `AddMapster`     | 程序集扫描、自动依赖注入、对象映射             |

## 集成步骤（核心代码模式）

### 1. 项目结构

Lzq.AgentForge.slnx
├── src/
│ ├── Lzq.AgentForge.WebApi/ # 启动项目
│ ├── Modules/AI/ # AI 模块（Application, Domain, Contracts）
│ ├── Modules/Rbac/ # RBAC 模块
│ └── AgentSkills/Lzq.AgentSkills.WorkOrder/ # AI 技能实现
├── Directory.Build.props # 统一版本控制
└── .editorconfig

``` text

### 2. Directory.Build.props（统一 NuGet 版本）

‍```xml
<Project>
  <PropertyGroup>
    <LzqExtensionsVersion>0.1.33</LzqExtensionsVersion>
    <MASAFrameworkVersion>1.2.0-preview.10</MASAFrameworkVersion>
    <MicrosoftPackageVersion>10.*</MicrosoftPackageVersion>
  </PropertyGroup>
</Project>
```

### 3. appsettings.json 配置结构

``` json
{
  "Logging": { ... },
  "DBConfigs": [
    { "Tag": "AgentForge", "DbType": "Sqlite", "ConnectionString": "Data Source=...", "CommandTimeOut": 30 },
    { "Tag": "Log", "DbType": "Sqlite", "ConnectionString": "Data Source=...", "CommandTimeOut": 30 }
  ],
  "AesKey": "xxxxxxxxxxxxxxxx",          // 加密密钥（用于数据库敏感字段）
  "AIKeySecret": {
    "SiliconFlow": "sk-...",
    "DeepSeek": "sk-..."
  },
  "ExternalApis": {
    "AuthApi": { "HttpHost": "https://...", "Timeout": 30 }
  }
}
```

> **开发环境**：`appsettings.Development.json` 会覆盖生产密钥，使用示例值即可。

### 4. Program.cs 完整构建流程

``` csharp
using Lzq.AgentForge.WebApi.Extensions;
using Lzq.Core;
using Lzq.Extensions.AI;
using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.EventBus;
using Lzq.Extensions.EventBus.RabbitMq;
using Lzq.Extensions.ExternalHttpApi;
using Lzq.Extensions.Jwt;
using Lzq.Extensions.NSwag;
using Lzq.Extensions.Serilog;
using Lzq.Extensions.SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog 日志（文件输出）
builder.AddLzqSerilog(options =>
{
    options.EnableFile = true;
    options.FilePath = "Logs/myapp-.log";
});

// 2. 加载核心程序集、Mapster 映射、自动依赖注入
builder.Services.AddCoreAssembly().AddMapster().AddCoreAutoInject();

// 3. 健康检查（自定义 + 基础）
builder.AddLzqHealthChecks();

// 4. NSwag（Swagger）文档 + UI 密码保护
builder.Services.AddLzqNSwag(options =>
{
    options.Documents = [new SwaggerDocumentInfo { Name = "WebApi", Title = "WebApi" }];
    options.Title = "WebApi";
    options.Version = "1.0.0";
    options.EnableSwaggerUI = !builder.Environment.IsProduction();
    options.EnableSwaggerUIPassword = true;
    options.SwaggerUIPassword = "123456";
    options.SwaggerUIPasswordCookieExpirationMinutes = 720;
});

// 5. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// 6. 外部 HTTP API 声明式客户端
builder.Services.AddExternalHttpApis(builder.Configuration);

// 7. JSON 序列化：long 转 string（防止前端精度丢失）
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new LongToStringConverter());
    options.SerializerOptions.Converters.Add(new LongNullableToStringConverter());
});

// 8. JWT 认证
builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    options.Issuer = "your-app";
    options.Audience = "your-app";
    options.SecurityKey = "your-secret-key-at-least-16-chars";
});

// 9. AI 能力 + 聊天历史存储（SqlSugar）+ Agent 技能
builder.Services.AddLzqAI()
    .AddSqlSugarChatHistoryProvider()
    .AddLzqAgentSkills();

// 10. SqlSugar 多数据库配置
builder.Services.AddLzqSqlSugar(builder.Configuration);

// 11. 事件总线（集成事件 + RabbitMQ 发布器 + 内存 Outbox 示例）
builder.Services.AddEventBus()
    .AddIntegrationEvent(option =>
    {
        option.UseMemoryOutbox();        // 开发用内存 Outbox，生产应替换为数据库 Outbox
        option.AddRabbitMqPublisher(opt =>
        {
            // 可配置 RabbitMQ 连接（若 appsettings 未配置则使用默认值 localhost）
            // opt.HostName = "rabbitmq";
        });
    });

// 12. 注册 Minimal APIs（必须在 Build 前最后调用）
builder.Services.AddCoreMinimalAPIs();

var app = builder.Build();

// 中间件管道
app.UseCors("AllowAll");
app.UseCoreExceptionHandler();          // 全局异常处理
app.UseLzqNSwag();                      // 启用 Swagger UI（带密码）
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapMasaMinimalAPIs();               // 映射所有 Minimal API 端点
app.Run();
```

### 5. 自定义健康检查扩展（参考示例）

``` csharp
// Extensions/HealthCheckExtensions.cs
public static class HealthCheckExtensions
{
    public static void AddLzqHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("服务运行正常"))
            .AddCheck<MemoryHealthCheck>("内存检查");
    }

    public static void MapLzqHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        }).AllowAnonymous();
    }
}

// 示例内存健康检查
public class MemoryHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken)
    {
        var memoryUsage = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
        return Task.FromResult(memoryUsage > 1024
            ? HealthCheckResult.Unhealthy($"内存使用率过高: {memoryUsage}MB")
            : HealthCheckResult.Healthy($"内存使用率正常: {memoryUsage}MB"));
    }
}
```

### 6. JSON 长整型转换器

当 API 返回 `long` 类型时，JavaScript 可能丢失精度，因此序列化为字符串：

``` csharp
public class LongToStringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), out var l) ? l : reader.GetInt64();

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
```

## 配置要点

### 多数据库配置（SqlSugar）

`DBConfigs` 数组中每个元素对应一个数据库实例，通过 `Tag` 区分。在 Repository 或 UnitOfWork 中通过 `[SqlSugarUnitOfWork(Tag = "AgentForge")]` 特性选择数据库。

### AI 密钥配置

`AIKeySecret` 节点支持多个 AI 提供商（如 SiliconFlow、DeepSeek）。`Lzq.Extensions.AI` 会读取这些密钥初始化 AI 客户端。

### 事件总线与 Outbox

- **内存 Outbox**：`option.UseMemoryOutbox()` 仅适用于开发环境，事件存储在内存队列中，应用重启会丢失。
- **生产 Outbox**：应实现基于数据库的 `IIntegrationEventStore`（参考 [eventbus-integration-outbox.md](https://./references/eventbus-integration-outbox.md)），并注册后台 `OutboxPublisherService` 调用 `RabbitMqPublisher`。

### JWT 配置

`AddLzqJwt` 需要 `appsettings.json` 中包含 JWT 相关节点（也可直接在代码中配置）。示例代码中使用了硬编码的 `Issuer` / `Audience` / `SecurityKey`，生产环境应使用配置或密钥管理服务。

## 扩展与自定义

- **新增 AI Agent 技能**：实现 `IAgentSkill` 接口，并放置在 `AgentSkills/` 文件夹下，`AddLzqAgentSkills()` 会自动扫描注册。
- **新增模块**：按照 `Modules/` 下的分层结构（Application、Domain、Contracts）创建新模块，并在 `Program.cs` 之前确保程序集被加载（`AddCoreAssembly` 会扫描所有引用的程序集）。
- **自定义管道行为**：在 `AddEventBus()` 后可通过 `cfg.AddOpenBehavior` 添加新的 MediatR 管道行为。

## 注意事项

1. **程序集扫描**：`AddCoreAssembly()` 和 `AddCoreAutoInject()` 依赖于 `MasaApp.GetAssemblies()`，确保所有需要扫描的程序集已加载（可通过 `builder.AddLzqMasaAssembly()` 手动添加）。
2. **事务与 Outbox 一致性**：如果使用了 `[UnitOfWork]` 特性，`IIntegrationEventStore` 必须与业务数据库使用相同的 `DbContext` 或事务范围。SqlSugar 版本中，`IUnitOfWork` 默认与当前 `Tag` 绑定的数据库连接关联。
3. **Swagger 密码保护**：生产环境建议启用，示例密码为 `123456`，请修改为强密码或集成现有认证。
4. **Minimal API 注册顺序**：`AddCoreMinimalAPIs()` 必须在 `builder.Services` 所有其他注册之后、`builder.Build()` 之前调用，以确保所有端点被扫描。
5. **CORS 策略**：示例使用了 `AllowAnyOrigin`，如果涉及 Cookie/认证，应改为具体来源或使用 `AllowCredentials`。

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