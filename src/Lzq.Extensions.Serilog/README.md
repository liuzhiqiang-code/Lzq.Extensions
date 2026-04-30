# Lzq.Extensions.Serilog

一个基于 Serilog 的 ASP.NET Core 日志扩展库，提供开箱即用的 TraceId 追踪、HTTP 请求上下文富化、多 Sink 灵活配置，并完全交由使用者通过 `SerilogOptions` 控制日志行为。

`Lzq.Core` 
## 🌟 核心特性

- **TraceId 自动附加**：通过 `ActivityTraceIdEnricher` 将 `Activity.Current.TraceId` 附加到每条日志，便于分布式追踪。
- **HTTP 请求上下文富化**：`HttpRequestEnricher` 自动添加当前 HTTP 请求的 `Method` 和 `Path` 到日志属性。
- **统一输出模板**：所有文本型 Sink（控制台、文件）共用同一个可配置的输出模板。
- **多 Sink 支持**：
  - 控制台（支持异步、自定义模板或 JSON 格式）
  - 文件（支持滚动策略、大小限制、保留数量）
  - SQLite（本地数据库存储）
  - Grafana Loki（集中式日志聚合）
- **完全配置驱动**：所有 Sink 的启用/禁用及参数均由 `SerilogOptions` 控制，不依赖环境变量判断。
- **与 ASP.NET Core 无缝集成**：通过 `AddLzqSerilog` 扩展方法一键替换默认日志提供程序。

## 🚀 快速开始
``` C#
var builder = WebApplication.CreateBuilder(args);
// 使用默认配置（控制台开启，输出模板为带时间的文本格式）
builder.AddLzqSerilog();

// 或者
builder.AddLzqSerilog(options =>
{
    options.MinimumLevel = Serilog.Events.LogEventLevel.Information;
    options.EnableFile = true;
    options.FilePath = "Logs/myapp-.log";
    options.FileSizeLimitBytes = 20 * 1024 * 1024; // 20 MB
    options.EnableLoki = true;
    options.LokiUrl = "http://loki:3100";
    options.LokiServiceName = "MyAppService";
});
```

### 全局设置

| 属性 | 类型 | 默认值 | 说明                           |
| ------ | ------ | -------- | -------------------------------- |
| `MinimumLevel`     | `LogEventLevel`     | `Debug`       | 全局最低日志级别               |
| `OutputTemplate`     | `string?`     | `"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [TraceId:{TraceId}] {Message:lj}{NewLine}{Exception}"`       | 统一输出模板，控制台和文件共用 |

### 控制台输出配置

| 属性 | 类型 | 默认值 | 说明                                     |
| ------ | ------ | -------- | ------------------------------------------ |
| `EnableConsole`     | `bool`     | `true`       | 是否启用控制台输出                       |
| `ConsoleAsync`     | `bool`     | `true`       | 是否对控制台输出使用异步包装（提高性能） |

### 文件输出配置

| 属性 | 类型 | 默认值 | 说明                         |
| ------ | ------ | -------- | ------------------------------ |
| `EnableFile`     | `bool`     | `false`       | 是否启用文件输出             |
| `FilePath`     | `string?`     | `null`       | 日志文件路径（如 `Logs/log-.txt`）          |
| `FileRollingInterval`     | `RollingInterval`     | `Day`       | 文件滚动间隔                 |
| `FileSizeLimitBytes`     | `long`     | `10 * 1024 * 1024`       | 单个文件大小上限（字节）     |
| `RetainedFileCountLimit`     | `int`     | `7`       | 保留的文件数量（按滚动周期） |
| `FileAsync`     | `bool`     | `true`       | 是否对文件输出使用异步包装   |

### SQLite 输出配置

| 属性 | 类型 | 默认值 | 说明                     |
| ------ | ------ | -------- | -------------------------- |
| `EnableSQLite`     | `bool`     | `false`       | 是否启用 SQLite 日志输出 |
| `SQLitePath`     | `string?`     | `"Logs/log.db"`       | SQLite 数据库文件路径    |

### Loki 输出配置

| 属性 | 类型 | 默认值 | 说明                   |
| ------ | ------ | -------- | ------------------------ |
| `EnableLoki`     | `bool`     | `false`       | 是否启用 Loki 日志推送 |
| `LokiUrl`     | `string?`     | `null`       | Loki 服务地址          |
| `LokiServiceName`     | `string`     | `"unknown_service"`       | Loki 标签中的服务名称  |