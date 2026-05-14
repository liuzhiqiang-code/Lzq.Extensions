# Serilog 配置参考 — `Lzq.Extensions.Serilog`

本文档深入说明 `Lzq.Extensions.Serilog` 内部配置机制、各 Sink 的行为细节、日志级别覆写规则以及输出模板的处理逻辑。

## 1. `SerilogOptions` 完整属性表

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| **全局设置** | | | |
| `MinimumLevel` | `LogEventLevel` | `Debug` | 全局最低日志级别，控制所有非覆写 Sink |
| `OutputTemplate` | `string` | 见代码 | 控制台、文件共用的输出模板。设置为 `null` 时控制台使用 `CompactJsonFormatter` |
| **控制台** | | | |
| `EnableConsole` | `bool` | `true` | 是否启用控制台输出 |
| `ConsoleAsync` | `bool` | `true` | `true` 时通过 `WriteTo.Async` 包装控制台 Sink |
| **文件** | | | |
| `EnableFile` | `bool` | `false` | 是否启用文件输出 |
| `FilePath` | `string?` | `null` | 日志文件路径（如 `"Logs/app-.log"`） |
| `FileRollingInterval` | `RollingInterval` | `Day` | 滚动间隔，控制新文件创建周期 |
| `FileSizeLimitBytes` | `long` | `10 * 1024 * 1024` | 单个文件大小上限 |
| `RetainedFileCountLimit` | `int` | `7` | 保留的文件数（按滚动间隔） |
| `FileAsync` | `bool` | `true` | `true` 时通过 `WriteTo.Async` 包装文件 Sink |
| **SQLite** | | | |
| `EnableSQLite` | `bool` | `false` | 是否启用 SQLite 输出 |
| `SQLitePath` | `string?` | `"Logs/log.db"` | SQLite 数据库文件路径 |
| **Loki** | | | |
| `EnableLoki` | `bool` | `false` | 是否启用 Grafana Loki 输出 |
| `LokiUrl` | `string?` | `null` | Loki 推送地址 |
| `LokiServiceName` | `string` | `"unknown_service"` | Loki 标签 `service_name` 的值 |

## 2. 日志级别覆写规则

`AddLzqSerilog` 在创建 LoggerConfiguration 时，固定设置以下覆盖，**不受 `SerilogOptions.MinimumLevel` 影响**：

- `Microsoft` → `LogEventLevel.Information`
- `Microsoft.AspNetCore` → `LogEventLevel.Warning`

这样做是为了抑制来自框架内部的低级别噪音（如健康检查、路由匹配等），同时仍保留应用程序自定义的 `MinimumLevel` 控制。

> 注意：这两个覆写是硬编码的，无法通过 Options 修改。如需调整，请直接 fork 库并重新构建。

## 3. 各 Sink 配置详解

### 3.1 控制台 Sink

- **启用条件**：`EnableConsole == true`
- **异步**：若 `ConsoleAsync == true`，使用 `WriteTo.Async(a => a.Console(...))`
- **输出格式**：
  - 若 `OutputTemplate` 不为空，使用 `outputTemplate: options.OutputTemplate`
  - 若 `OutputTemplate` 为 `null`，使用 `Serilog.Formatting.Compact.CompactJsonFormatter()`
- **最低级别**：遵循全局 `MinimumLevel`（无单独覆写）

### 3.2 文件 Sink

- **启用条件**：`EnableFile == true` 且 `FilePath` 不为空
- **异步**：若 `FileAsync == true`，使用 `WriteTo.Async(a => a.File(...))`
- **最低级别**：**强制使用 `LogEventLevel.Debug`**，以确保所有调试信息落入文件，即使全局 `MinimumLevel` 设置得更高。这通过参数 `restrictedToMinimumLevel: LogEventLevel.Debug` 显式指定。
- **输出格式**：使用 `options.OutputTemplate`（若为 `null` 则使用 Serilog.Sinks.File 的内置默认模板，不强制 JSON）
- **文件滚动与限制**：
  - `rollingInterval`: 来自 `FileRollingInterval`
  - `fileSizeLimitBytes`: 来自 `FileSizeLimitBytes`
  - `retainedFileCountLimit`: 来自 `RetainedFileCountLimit`

### 3.3 SQLite Sink

- **启用条件**：`EnableSQLite == true` 且 `SQLitePath` 不为空
- **最低级别**：遵循全局 `MinimumLevel`
- **不支持异步**：直接同步写入，无 `WriteTo.Async` 包装
- **存储路径**：`sqliteDbPath: options.SQLitePath`
- **表结构**：由 `Serilog.Sinks.SQLite` 自动创建 `Logs` 表，包含时间戳、级别、异常、消息等标准列

### 3.4 Grafana Loki Sink

- **启用条件**：`EnableLoki == true` 且 `LokiUrl` 不为空
- **最低级别**：遵循全局 `MinimumLevel`
- **不支持异步**：直接同步推送
- **标签设置**：
  ```csharp
  labels: [new LokiLabel { Key = "service_name", Value = options.LokiServiceName }]
  ```

  仅自动添加 `service_name` 标签，其他标签需自行扩展 Sink 配置（需修改库代码或额外调用 `WriteTo.GrafanaLoki` 覆盖）。

## 4. Enricher 注册顺序

在 `AddLzqSerilog` 内部，Enricher 的添加顺序如下：

1. `.Enrich.FromLogContext()` — 从 `ILogger.BeginScope` 或 `LogContext.PushProperty` 中抽取属性
2. `.Enrich.With<ActivityTraceIdEnricher>()` — 添加 `TraceId` 属性
3. `.Enrich.With<HttpRequestEnricher>()` — 添加 `Method` 和 `Path` 属性

因此，如果同一条日志在 Scope 中也设置了 `TraceId`、`Method` 或 `Path`，Enricher 中使用的 `AddPropertyIfAbsent` 方法会**优先保留 Scope 中的值**，不会被覆盖。

## 5. `OutputTemplate` 的默认值

默认输出模板为：

``` text
{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [TraceId:{TraceId}] {Message:lj}{NewLine}{Exception}
```

各部分含义：

- `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}` — 精确到毫秒的时间戳，带时区偏移
- `[{Level:u3}]` — 三位大写级别缩写（如 INF、WRN、ERR）
- `[TraceId:{TraceId}]` — 分布式追踪 ID
- `{Message:lj}` — 日志消息，`:lj` 用于输出结构化数据的字面值
- `{NewLine}` — 换行
- `{Exception}` — 异常详细信息（若存在）

该模板适用于文本型 Sink（控制台、文件），SQLite 和 Loki 则有自己的存储格式，不受此模板控制。

## 6. 全局静态 Logger

执行 `builder.AddLzqSerilog()` 后，会设置全局静态属性：

``` csharp
Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();
```

这意味着应用程序启动早期（如 `Program.cs` 中最开始）也可以使用 `Log.Information(...)` 记录日志，但这些日志不会经过 Enricher 富化，因为 `IHttpContextAccessor` 尚未生效。建议在 `builder.Build()` 之后使用 `ILogger<T>` 注入方式记录请求相关日志。

## 7. 配置验证

本库不执行配置验证（如检查文件路径是否存在、Loki URL 是否可达）。如果配置错误：

- 文件路径无效 → 文件 Sink 会静默失败（Serilog 默认行为），可通过 `SelfLog.Enable` 监听错误。
- Loki URL 不可达 → Sink 会抛出异常或丢失日志（取决于 Sink 实现）。
- SQLite 路径不可写 → Sink 在首次写入时抛出异常。

建议在部署前手动验证配置。