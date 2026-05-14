# Serilog Enricher 与中间件参考 — `Lzq.Extensions.Serilog`

本文档详细说明 `Lzq.Extensions.Serilog` 提供的两个日志增强器（Enricher）和一个 HTTP 日志中间件的实现原理、依赖关系和内建行为。

## 1. `ActivityTraceIdEnricher`

### 1.1 作用

自动将当前 `System.Diagnostics.Activity.Current?.TraceId` 附加到每条日志事件的 `TraceId` 属性上，实现日志与分布式追踪的关联。

### 1.2 实现细节

```csharp
public class ActivityTraceIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
        var property = propertyFactory.CreateProperty("TraceId", traceId);
        logEvent.AddPropertyIfAbsent(property);
    }
}
```

- **读取** **`TraceId`**：`Activity.Current` 来自 `System.Diagnostics`，代表当前活动的 Span 上下文。若应用未集成 OpenTelemetry 或未创建 Activity，则 `Activity.Current` 为 `null`，`TraceId` 被设为空字符串 `""`。
- **使用** **`AddPropertyIfAbsent`**：确保不在日志事件中重复添加同名属性。若其他 Enricher 或 `LogContext` 已设置 `TraceId`，将**保留已有值**。

### 1.3 注册方式

Enricher 在 `AddLzqSerilog` 中通过以下代码注册：

``` csharp
loggerConfig.Enrich.With<ActivityTraceIdEnricher>();
```

本库还注册了 `Enrich.FromLogContext()`，其顺序在 `ActivityTraceIdEnricher` 之前。因此，若应用代码通过 `using (LogContext.PushProperty("TraceId", customValue))` 显式设置了 TraceId，Enricher 不会覆盖它。

### 1.4 依赖与前置条件

- 需要 `System.Diagnostics.DiagnosticSource` 包（[ASP.NET](https://asp.net/) Core 默认包含）。
- 若要获得真实的 `TraceId`，应用必须启用 OpenTelemetry 或手动创建 `Activity`，例如：

  ``` csharp
  using var activity = new ActivitySource("MyApp").StartActivity("Process");
  ```

  或者在 [ASP.NET](https://asp.net/) Core 中自动由 `AddOpenTelemetry()` 中间件生成。
- 若未满足上述条件，所有日志的 `TraceId` 将为空字符串，不会引发异常。

## 2. `HttpRequestEnricher`

### 2.1 作用

在每条日志中自动附加当前 HTTP 请求的 `Method` 和 `Path` 属性，方便在集中式日志系统中按 API 路径筛选。

### 2.2 实现细节

``` csharp
public class HttpRequestEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestEnricher() { }

    public HttpRequestEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null) return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Method", httpContext.Request.Method));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Path", httpContext.Request.Path));
    }
}
```

- **依赖** **`IHttpContextAccessor`**：通过构造函数注入。`AddLzqSerilog` 内部已调用 `builder.Services.AddHttpContextAccessor()`，确保该服务可用。
- **空值安全**：当 `_httpContextAccessor` 为 `null` 或 `HttpContext` 为 `null`（如后台线程、控制台应用、启动阶段）时，直接返回，不附加任何属性。
- **属性键名**：固定为 `"Method"` 和 `"Path"`，与 `ILogger` 结构化模板中的名称可直接匹配。
- **使用** **`AddPropertyIfAbsent`**：若日志已通过 `LogContext.PushProperty("Method", ...)` 设置了这些属性，Enricher 不会覆盖。

### 2.3 注入与解析

Serilog 在解析 Enricher 时，会尝试使用 DI 容器构造对象。由于 `HttpRequestEnricher` 提供了无参构造函数和带 `IHttpContextAccessor` 参数的构造函数，Serilog 会优先使用带参构造函数并从容器中解析 `IHttpContextAccessor`。因此，只要项目中注册了 `IHttpContextAccessor`，Enricher 就能正常工作。

### 2.4 应用场景限制

- 仅在 HTTP 请求处理过程中（`HttpContext` 存在）有效。
- 对于后台任务、消息队列消费者、应用启动日志等，这些属性不会被添加，日志中仅保留 TraceId 等其他属性。
- 若要为后台作业也添加类似路径信息，请使用 `LogContext.PushProperty` 手动设置。

## 3. `HttpLoggingMiddleware`

### 3.1 作用

自动记录所有 `/api` 路径下的 HTTP 请求，包含方法、路径、状态码和耗时（毫秒），异常时记录错误日志并保留异常传播。

### 3.2 中间件管道位置

建议尽早注册，以便精确测量整个管道的耗时，位于 `app.UseRouting()` 之后或 `app.MapControllers()` 之前均可。

``` csharp
app.UseMiddleware<Lzq.Extensions.Serilog.HttpLoggingMiddleware>();
```

### 3.3 实现细节

``` csharp
public class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpLoggingMiddleware> _logger;

    public HttpLoggingMiddleware(RequestDelegate next, ILogger<HttpLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
            sw.Stop();

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HTTP {Method} {Path} failed after {ElapsedMs}ms",
                context.Request.Method,
                context.Request.Path,
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

#### 关键行为

1. **计时**：使用 `Stopwatch` 从进入 `Invoke` 开始计时，到请求结束或异常抛出停止。
2. **路径过滤**：仅记录 `context.Request.Path.StartsWithSegments("/api")` 为 true 的请求。其他路径（如 `/health`、`/swagger`）不会产生日志。
3. **正常请求日志**：

    - 模板：`"HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms"`
    - 使用 `LogInformation` 级别。
4. **异常请求日志**：

    - 模板：`"HTTP {Method} {Path} failed after {ElapsedMs}ms"`
    - 使用 `LogError` 级别，并将异常作为第一个参数传入，保留完整堆栈。
    - **重新抛出异常**（`throw;`），不吞掉异常，确保后续异常处理中间件（如 `UseExceptionHandler`）仍能处理。
5. **日志类别**：使用 `ILogger<HttpLoggingMiddleware>`，便于通过 Serilog 过滤器按类别过滤或调整级别。

### 3.4 日志输出示例

- 正常请求：

  ``` text
  [INF] HTTP GET /api/users responded 200 in 42ms
  ```
- 异常请求：

  ``` text
  [ERR] HTTP POST /api/orders failed after 120ms
  System.InvalidOperationException: ...
  ```

### 3.5 与其他中间件的协同

- 如果同时使用了自定义的全局异常处理中间件，异常的 `LogError` 会先记录，然后异常重新抛出，全局异常处理中间件仍可捕获并转换为统一响应格式。
- 若应用中已包含请求日志记录（如 `UseHttpLogging`），建议只选用一个，避免重复日志。

## 4. Enricher 与中间件的协同

当 `HttpLoggingMiddleware` 记录日志时，`HttpRequestEnricher` 会为该日志事件附加 `Method` 和 `Path` 属性。因此，中间件的日志模板中虽已显式包含 `{Method}` 和 `{Path}`，这两个值也会作为结构化属性单独存储，便于在 Loki 等系统中直接索引。`TraceId` 同样被附加，实现从 HTTP 日志到业务日志的 TraceId 关联。

## 5. 故障排查

| 问题                 | 可能原因                                         | 解决方法                                                                                                       |
| ---------------------- | -------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `TraceId` 始终为空            | 未启用 OpenTelemetry 或未创建 Activity           | 检查 `services.AddOpenTelemetry()` 或手动启动 Activity                                                                                      |
| `Method` 和 `Path` 缺失            | 日志在非 HTTP 上下文中发出，或 `IHttpContextAccessor` 未正确注册       | 确保 `builder.Services.AddHttpContextAccessor()` 被调用（`AddLzqSerilog` 已调用）                                                                                        |
| 中间件日志未出现     | 请求路径不以 `/api` 开头，或中间件未注册               | 检查 `app.UseMiddleware<...>()` 位置和请求路径                                                                                           |
| 文件日志未记录 `Debug` 级别 | 文件 Sink 强制 `Debug`，但全局 MinimumLevel 可能设为 `Error`？ | 文件 Sink 内部已覆写为 `Debug`，不受全局限制，请检查 Sink 配置是否启用异步包装时使用了错误的 `restrictedToMinimumLevel`（库代码已固定为 Debug） |

## 6. 自定义与扩展

若要自定义 Enricher 或中间件行为：

- **添加更多请求属性**：创建自定义 `ILogEventEnricher`，通过 `builder.Host.UseSerilog((ctx, config) => config.Enrich.With<YourEnricher>())` 添加，但需注意 Enricher 顺序。
- **修改中间件过滤路径**：可继承 `HttpLoggingMiddleware` 或复制代码并修改 `if (context.Request.Path.StartsWithSegments("/api"))` 条件。
- **调整中间件日志级别**：使用 Serilog 的 `MinimumLevel.Override("Lzq.Extensions.Serilog.HttpLoggingMiddleware", LogEventLevel.Warning)` 降低级别。