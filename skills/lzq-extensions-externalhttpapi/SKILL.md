---
name: lzq-extensions-externalhttpapi
description: 使用 Lzq.Extensions.ExternalHttpApi 库以声明式接口快速集成外部 HTTP API，支持自动注册、请求响应日志、统一响应解包、身份信息透传。适用于 .NET 应用中需要调用多个下游服务并希望减少样板代码的场景。
license: Proprietary
compatibility: 需要 .NET 6+、Lzq.Extensions.ExternalHttpApi NuGet 包、WebApiClientCore 2.1.5+ 以及 Lzq.Core 基础库
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.ExternalHttpApi` 基于 **WebApiClientCore** 封装，提供声明式的外部 HTTP API 客户端。通过定义接口继承 `IExternalHttpApi`，并在 `appsettings.json` 中配置基地址和超时时间，即可自动注册客户端。内置 AOP 过滤器可实现请求/响应日志记录、统一包装解包（`ApiResult<T>` → `T`）、以及根据 `NeedAuthToken` 特性自动透传当前请求的身份信息（Token、TraceId、租户 ID 等）。

在以下场景使用本技能：
- 需要调用多个外部 REST API，希望用接口定义代替手动 `HttpClient` 调用。
- 希望统一处理外部 API 的响应包装（如 `{code, message, data}`），自动提取 `data`。
- 希望在调用外部服务时自动记录请求 URL 和响应状态码。
- 希望自动将当前请求的 `Authorization` 和追踪头传递到下游服务。
- 需要通过配置管理不同外部服务的基地址，并强制校验配置完整性。

## 何时使用

- 用户要求“调用外部 HTTP 接口”、“声明式 HTTP 客户端”、“自动注册 WebApiClient”。
- 提到 `Lzq.Extensions.ExternalHttpApi`、`AddExternalHttpApis`、`IExternalHttpApi`。
- 需要在多个服务之间透传身份认证信息或追踪 ID。
- 需要简化 `HttpClient` 生命周期管理及异常处理。

## 何时不使用

- 仅调用单个或少数几个 API，且无需声明式接口和 AOP 时，直接使用 `HttpClient` 或 `IHttpClientFactory` 即可。
- 已使用其他 HTTP 客户端库（如 Refit、RestSharp），且无迁移需求。
- 不需要响应解包、身份透传、日志记录等附加功能。
- 未使用 Masa BuildingBlocks 或 `Lzq.Core` 提供的程序集扫描（可自定义但需额外工作）。

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.ExternalHttpApi
```

依赖项（自动安装）：

- `WebApiClientCore` (\>\= 2.1.5) – 声明式 HTTP 客户端核心库
- `Lzq.Core` – 提供 `MasaApp.GetAssemblies()` 等基础能力

### 2. 配置 `appsettings.json`

在配置文件中添加 `ExternalApis` 节点，为每个外部 API 指定 `HttpHost` 和可选超时：

``` json
{
  "ExternalApis": {
    "UserApi": {
      "HttpHost": "https://api.example.com",
      "Timeout": 30
    },
    "OrderApi": {
      "HttpHost": "https://order.example.com"
    }
  }
}
```

- 配置键名：接口名去掉首字母 `I`（如 `IUserApi` → `UserApi`）。若需自定义，可使用 `[ExternalHttpApiConfig("MyKey")]` 特性。
- `Timeout` 单位为秒，默认 100 秒。

### 3. 定义 API 接口

创建一个继承 `IExternalHttpApi` 的接口，并添加所需的方法及过滤器：

``` csharp
using Lzq.Extensions.ExternalHttpApi;
using Lzq.Extensions.ExternalHttpApi.Aop;
using WebApiClientCore.Attributes;

public interface IUserApi : IExternalHttpApi
{
    [HttpGet("api/users/{id}")]
    [ApiLogging]               // 记录请求/响应日志
    [ApiReturnUnwrapper]       // 自动解包 ApiResult<T>
    Task<UserDto> GetUserAsync(int id);
}
```

如果需要透传当前用户的认证头，在接口上添加 `[NeedAuthToken]`：

``` csharp
[NeedAuthToken]
public interface IOrderApi : IExternalHttpApi
{
    [HttpGet("api/orders")]
    Task<List<OrderDto>> GetOrdersAsync();
}
```

### 4. 注册服务

在 `Program.cs` 或 Startup 类中：

``` csharp
builder.Services.AddExternalHttpApis(builder.Configuration);
```

内部行为：

- 注册 `IHttpContextAccessor` 和 `HttpContextHeaderFilter`。
- 扫描 `MasaApp.GetAssemblies()` 中所有 `IExternalHttpApi` 接口。
- 为每个接口读取配置并注册强类型客户端，若缺少 `HttpHost` 或 URL 不合法则抛出异常，阻止启动。

### 5. 使用客户端

在任何地方通过构造函数注入接口即可调用：

``` csharp
public class MyService
{
    private readonly IUserApi _userApi;
    public MyService(IUserApi userApi) => _userApi = userApi;

    public async Task<UserDto> GetUser(int id)
        => await _userApi.GetUserAsync(id);
}
```

## AOP 过滤器详解

### `ApiLoggingAttribute`

- **适用位置**：接口或方法。
- **行为**：请求时记录 `请求地址: {Url}`，响应时记录 `响应状态: {StatusCode}`（使用 `ILogger<ApiLoggingAttribute>`）。
- 可通过在接口方法上添加或移除此特性按需启用。

### `ApiReturnUnwrapperAttribute`

- **适用位置**：方法。
- **行为**：

  1. 检查 HTTP 状态码，非成功时跳过。
  2. 将响应体反序列化为 `ApiResult<T>`（需符合 `{ Code, Message, Data }` 结构）。
  3. 若 `Code != 200`，抛出 `Exception($"业务异常: {Message} ({Code})")`。
  4. 若成功，将 `context.Result` 替换为 `result.Data`，此后调用方直接得到 `T` 类型对象，无需手动解包。
- **注意**：内部使用 `dynamic` 和 `JsonDeserializeAsync`，确保返回类型正确。

### `HttpContextHeaderFilter`（全局过滤器）

- **触发条件**：接口标注了 `[NeedAuthToken]`。
- **行为**：从当前 `HttpContext` 中提取并添加以下请求头到出站请求：

  - `X-Trace-Id` ← `HttpContext.TraceIdentifier`
  - `Authorization` ← 原始请求的 `Authorization` 头
  - `x-tenant-id` ← 原始请求的自定义头 `__tenant__`
- 该过滤器通过 DI 获取 `IHttpContextAccessor`，需确保请求处于 HTTP 上下文。

## 配置参考

| 配置节 | 键 | 类型 | 必填 | 说明                                               |
| -------- | ---- | ------ | ------ | ---------------------------------------------------- |
| `ExternalApis:<ApiKey>`       | `HttpHost`   | `string`     | 是   | 外部 API 基地址，必须是合法的绝对 HTTP/HTTPS URL。 |
| `ExternalApis:<ApiKey>`       | `Timeout`   | `double`     | 否   | 超时时间（秒），默认 100。                         |

- `ApiKey` 默认等于接口名去掉首字母 `I`，可通过 `[ExternalHttpApiConfig("CustomKey")]` 覆盖。

## 程序集扫描依赖

`AddExternalHttpApis` 默认使用 `MasaApp.GetAssemblies()` 获取当前所有程序集。若未使用 Masa BuildingBlocks，可调用重载方法并手动传入程序集数组：

``` csharp
services.AddExternalHttpApis(configuration, typeof(MyApi).Assembly);
```

## 注意事项

- **启动校验**：缺失 `ExternalApis` 配置节或任一接口的 `HttpHost` 无效，应用将在启动时抛出 `InvalidOperationException`，拒绝启动。
- **响应解包**：`ApiReturnUnwrapperAttribute` 假设外部 API 返回 `{ code: 200, message: "...", data: ... }` 格式。若外部 API 结构不同，请自行实现类似的过滤器。
- **异常处理**：解包时业务异常会直接抛出通用 `Exception`，可在全局异常中间件中统一捕获并转换为友好响应。
- **多租户透传**：`HttpContextHeaderFilter` 会尝试读取请求头 `__tenant__`，若业务中租户标识不同，需调整源码或扩展过滤器。
- **日志记录**：`ApiLoggingAttribute` 使用 `ILogger<ApiLoggingAttribute>`，可通过 Serilog 等日志框架按需过滤或设定级别。
- **性能**：所有过滤器均异步执行，对出站请求延迟影响极小；响应解包使用系统文本 JSON 反序列化，复杂 DTO 需确保可序列化。
- **配置热更新**：默认不支持自动刷新配置，接口的 `HttpHost` 在注册时确定。如需动态切换，请使用 `IHttpApiFactory` 或自定义管理。

## 扩展点

- **自定义过滤器**：实现 `IApiFilter` 或继承 `ApiFilterAttribute`，按需添加到接口/方法或全局。
- **替换异常类型**：在 `ApiReturnUnwrapperAttribute` 中可将 `throw new Exception(...)` 替换为项目的 `UserFriendlyException` 或其他业务异常。
- **动态 Host**：可在 `AddHttpApi` 的配置委托中通过 `provider` 获取其他服务，实现动态基地址逻辑。