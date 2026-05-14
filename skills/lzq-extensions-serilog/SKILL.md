---
name: lzq-extensions-serilog
description: 使用 Lzq.Extensions.Serilog 库为 ASP.NET Core 应用集成结构化日志，支持 TraceId 追踪、HTTP 上下文富化、多 Sink 灵活配置（控制台/文件/SQLite/Loki）。适用于需要开箱即用的分布式日志、请求追踪及集中式日志聚合的场景。
license: Proprietary
compatibility: 需要 .NET 8+、Lzq.Extensions.Serilog NuGet 包及 Serilog 生态
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.Serilog` 基于 **Serilog** 封装，为 ASP.NET Core 应用提供一键式结构化日志集成。它自动将 `Activity.Current.TraceId` 附加到每条日志，支持 HTTP 请求上下文富化，并通过 `SerilogOptions` 统一管理控制台、文件、SQLite、Grafana Loki 等多 Sink 的输出行为。

在以下场景使用本技能：
- 需要为 ASP.NET Core 应用快速接入 Serilog 结构化日志。
- 需要自动记录分布式 TraceId，实现日志与调用链关联。
- 需要根据环境灵活开关控制台、文件、SQLite 或 Loki 日志输出。
- 需要自动在日志中附带当前 HTTP 请求的 Method 和 Path。
- 需要通过中间件自动记录 `/api` 路径下的请求与响应耗时。

## 何时使用

- 用户要求“接入 Serilog”、“添加日志追踪”、“配置 Loki 日志”等。
- 提到 `Lzq.Extensions.Serilog`、`AddLzqSerilog`、`SerilogOptions`。
- 需要统一日志输出模板，包含 TraceId、时间戳等关键信息。
- 需要将日志同时输出到多个目标（控制台 + 文件 + Loki）。

## 何时不使用

- 纯客户端应用（非 ASP.NET Core）。
- 已使用其他日志框架（如 NLog、log4net），且无迁移计划。
- 不需要 TraceId 或 HTTP 上下文富化，且偏好自行从零配置 Serilog。

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.Serilog
```

此包包含以下传递依赖，无需单独安装：

- `Serilog` / `Serilog.AspNetCore`
- `Serilog.Sinks.Async`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`
- `Serilog.Sinks.SQLite`
- `Serilog.Sinks.Grafana.Loki`

### 2. 基础配置（代码驱动）

本库不依赖 `appsettings.json`，所有配置通过 `SerilogOptions` 在代码中完成：

``` csharp
var builder = WebApplication.CreateBuilder(args);

// 使用默认配置：控制台开启，带 TraceId 的文本模板
builder.AddLzqSerilog();
```

### 3. 完整自定义配置

``` csharp
builder.AddLzqSerilog(options =>
{
    // 全局最低日志级别
    options.MinimumLevel = LogEventLevel.Information;

    // 控制台配置
    options.EnableConsole = true;
    options.ConsoleAsync = true;

    // 文件配置
    options.EnableFile = true;
    options.FilePath = "Logs/app-.log";
    options.FileSizeLimitBytes = 20 * 1024 * 1024; // 20 MB
    options.RetainedFileCountLimit = 14;
    options.FileRollingInterval = RollingInterval.Day;
    options.FileAsync = true;

    // SQLite 配置
    options.EnableSQLite = true;
    options.SQLitePath = "Logs/app.db";

    // Grafana Loki 配置
    options.EnableLoki = true;
    options.LokiUrl = "http://loki:3100";
    options.LokiServiceName = "MyAppService";
});
```

### 4. 注册 HTTP 日志中间件（可选）

在 `Program.cs` 中注册中间件，自动记录 `/api` 路径下的所有请求：

``` csharp
var app = builder.Build();

app.UseMiddleware<Lzq.Extensions.Serilog.HttpLoggingMiddleware>();

// 其他中间件...
app.Run();
```

中间件行为：

- 记录 `/api` 路径下所有请求的 Method、Path、StatusCode 及耗时（毫秒）。
- 请求发生异常时以 `LogError` 记录，并重新抛出异常，保留原始调用栈。

### 5. 使用日志

在任意类中通过标准 `ILogger<T>` 写入日志：

``` csharp
public class UserService
{
    private readonly ILogger<UserService> _logger;

    public UserService(ILogger<UserService> logger) => _logger = logger;

    public void CreateUser(string name)
    {
        _logger.LogInformation("Creating user {UserName}", name);
        // 业务逻辑...
    }
}
```

每条日志自动附加以下属性：

| 属性 | 来源 | 说明                                    |
| ------ | ------ | ----------------------------------------- |
| `TraceId`     | `Activity.Current.TraceId`     | 分布式追踪 ID，无 Activity 时为空字符串 |
| `Method`     | `IHttpContextAccessor`     | 当前 HTTP 请求方法（如 GET、POST）      |
| `Path`     | `IHttpContextAccessor`     | 当前 HTTP 请求路径                      |

## 配置参考 (`SerilogOptions`)

### 全局设置

| 属性 | 类型 | 默认值 | 说明                                            |
| ------ | ------ | -------- | ------------------------------------------------- |
| `MinimumLevel`     | `LogEventLevel`     | `Debug`       | 全局最低日志级别。`Microsoft` 和 `Microsoft.AspNetCore` 分别被覆写为 `Information` 和 `Warning`        |
| `OutputTemplate`     | `string`     | `"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [TraceId:{TraceId}] {Message:lj}{NewLine}{Exception}"`       | 控制台和文件共用的输出模板。设为 `null` 时控制台使用 `CompactJsonFormatter` |

### 控制台输出

| 属性 | 类型 | 默认值 | 说明                         |
| ------ | ------ | -------- | ------------------------------ |
| `EnableConsole`     | `bool`     | `true`       | 是否启用控制台输出           |
| `ConsoleAsync`     | `bool`     | `true`       | 是否使用异步包装提升写入性能 |

### 文件输出

| 属性 | 类型 | 默认值 | 说明                                |
| ------ | ------ | -------- | ------------------------------------- |
| `EnableFile`     | `bool`     | `false`       | 是否启用文件输出                    |
| `FilePath`     | `string?`     | `null`       | 日志文件路径（如 `Logs/log-.txt`），支持滚动占位符 |
| `FileRollingInterval`     | `RollingInterval`     | `Day`       | 文件滚动间隔                        |
| `FileSizeLimitBytes`     | `long`     | `10 * 1024 * 1024`       | 单个文件大小上限（字节）            |
| `RetainedFileCountLimit`     | `int`     | `7`       | 保留的文件数量（按滚动周期）        |
| `FileAsync`     | `bool`     | `true`       | 是否对文件输出使用异步包装          |

> 文件 Sink 内部强制最低级别为 `Debug`，确保所有日志落入文件，不受全局 `MinimumLevel` 影响。

### SQLite 输出

| 属性 | 类型 | 默认值 | 说明                     |
| ------ | ------ | -------- | -------------------------- |
| `EnableSQLite`     | `bool`     | `false`       | 是否启用 SQLite 日志输出 |
| `SQLitePath`     | `string?`     | `"Logs/log.db"`       | SQLite 数据库文件路径    |

SQLite Sink 使用全局 `MinimumLevel` 作为最低级别。

### Grafana Loki 输出

| 属性 | 类型 | 默认值 | 说明                      |
| ------ | ------ | -------- | --------------------------- |
| `EnableLoki`     | `bool`     | `false`       | 是否启用 Loki 日志推送    |
| `LokiUrl`     | `string?`     | `null`       | Loki 服务地址             |
| `LokiServiceName`     | `string`     | `"unknown_service"`       | Loki 标签中的服务名称（`service_name`） |

## Enricher 说明

### ActivityTraceIdEnricher

- **触发时机**：每条日志写入时。
- **行为**：读取 `System.Diagnostics.Activity.Current?.TraceId`，将其作为 `TraceId` 属性附加到日志事件。
- **依赖**：无需额外注册，由 `AddLzqSerilog` 自动添加。
- **注意**：需确保应用已启用 `ActivitySource` 或接入了 OpenTelemetry，否则 `TraceId` 为空字符串。

### HttpRequestEnricher

- **触发时机**：每条日志写入时，且当前处于 HTTP 请求上下文中。
- **行为**：通过 `IHttpContextAccessor` 获取当前 `HttpContext`，附加 `Method` 和 `Path` 属性。
- **依赖**：`AddLzqSerilog` 内部已调用 `builder.Services.AddHttpContextAccessor()`，无需手动注册。
- **注意**：在非 HTTP 上下文（如后台任务、控制台应用）中，Enricher 安全跳过，不附加任何属性。

## 中间件说明 (`HttpLoggingMiddleware`)

- **过滤规则**：仅记录 `context.Request.Path.StartsWithSegments("/api")` 为 true 的请求。
- **正常请求日志模板**：

  ``` text
  HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms
  ```
- **异常请求日志模板**：

  ``` text
  HTTP {Method} {Path} failed after {ElapsedMs}ms
  ```

  异常会被重新抛出，确保上层错误处理中间件仍能捕获。
- **性能**：使用 `Stopwatch` 精确计时，对请求管道额外开销极小。

## 注意事项

- **日志级别覆写**：`Microsoft` 命名空间最低级别被固定为 `Information`，`Microsoft.AspNetCore` 为 `Warning`，不受 `MinimumLevel` 配置影响。此举旨在减少框架噪音。
- **文件 Debug 级别**：文件 Sink 内部强制使用 `LogEventLevel.Debug`，确保所有日志落入文件，避免丢失调试信息。
- **OutputTemplate 与 JSON 格式**：当 `OutputTemplate` 为 `null` 时，控制台使用 `Serilog.Formatting.Compact.CompactJsonFormatter` 输出结构化 JSON；文件 Sink 在 `OutputTemplate` 为 `null` 时使用其自身的默认模板。
- **异步写入**：控制台和文件均支持异步包装（`Serilog.Sinks.Async`），建议保持开启以避免日志写入阻塞主线程。
- **SQLite 与 Loki**：这两个 Sink 不支持异步包装，直接同步写入。在高吞吐场景下需评估性能影响。
- **HttpRequestEnricher 生效条件**：必须在 HTTP 请求上下文中才能富化 `Method` 和 `Path`。后台服务、消息队列消费者等场景中这些属性不会被添加。
- **静态 Logger**：`Log.Logger` 会在 `AddLzqSerilog` 中被设置为全局静态 Logger，可用于应用启动阶段的早期日志记录。

## 参考资料

- `references/serilog-configuration-reference.md` — SerilogOptions 详细配置说明、Sink 内部机制及日志级别覆写规则。
- `references/serilog-enricher-middleware.md` — ActivityTraceIdEnricher 与 HttpRequestEnricher 工作原理，HttpLoggingMiddleware 请求过滤与异常处理流程。