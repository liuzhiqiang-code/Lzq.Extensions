Lzq.Extensions.ExternalHttpApi
基于 WebApiClientCore 的声明式外部 HTTP API 客户端扩展库。
通过接口定义 + 配置的方式，快速集成、调用第三方服务，并内置了日志记录、身份信息透传、统一响应解包等 AOP 能力。

主要特性
扫描程序集中继承 IExternalHttpApi 的接口，自动注册为 HTTP 客户端

基于 appsettings.json 的约定配置（基地址 HttpHost、超时等）

请求/响应过滤器（IApiFilter）：

ApiLoggingAttribute – 请求/响应日志记录

ApiReturnUnwrapperAttribute – 自动将 ApiResult<T> 解包装，只返回 Data 字段，非成功码抛出业务异常

HttpContextHeaderFilter – 根据 NeedAuthToken 特性条件性地透传 Authorization, TraceId, TenantId 等请求头

支持通过 ExternalHttpApiConfigAttribute 自定义配置节点名称

强类型配置校验（缺失 HttpHost 或 URL 格式错误将启动失败）

依赖
.NET (Core) 3.1+ / .NET 5+

WebApiClientCore

Masa.BuildingBlocks（提供 MasaApp.GetAssemblies() 程序集扫描；若不想依赖 Masa，可自行修改扩展方法中的程序集来源）

Microsoft.AspNetCore.Http.Abstractions（IHttpContextAccessor）

Microsoft.Extensions.* 系列（配置、日志、DI）

快速开始
1. 安装包
bash
# 核心库
dotnet add package WebApiClientCore
# 若使用 Masa 程序集扫描
dotnet add package Masa.BuildingBlocks
# 添加本扩展项目或 DLL 引用
2. 配置 appsettings.json
json
{
  "ExternalApis": {
    "UserApi": {
      "HttpHost": "https://api.example.com",
      "Timeout": 30   // 秒
    },
    "OrderApi": {
      "HttpHost": "https://order.example.com"
    }
  }
}
约定：接口名为 IUserApi 则配置键为 UserApi（自动去掉首字母 I）。
也可使用 [ExternalHttpApiConfig("xxx")] 自定义键名。

3. 定义 API 接口
csharp
using Lzq.Extensions.ExternalHttpApi;
using Lzq.Extensions.ExternalHttpApi.Aop;
using WebApiClientCore.Attributes;

public interface IUserApi : IExternalHttpApi
{
    [HttpGet("api/users/{id}")]
    [ApiLogging]               // 记录日志
    [ApiReturnUnwrapper]       // 自动解包 ApiResult<T>
    Task<UserDto> GetUserAsync(int id);
}
csharp
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}
csharp
// 如果接口需要透传当前请求的身份信息，添加特性
[NeedAuthToken]
public interface IOrderService : IExternalHttpApi
{
    [HttpGet("api/orders")]
    Task<List<OrderDto>> GetOrdersAsync();
}
4. 注册服务
在 Startup.cs 或 Program.cs 中：

csharp
using Lzq.Extensions.ExternalHttpApi;

// 在 ConfigureServices 中
services.AddExternalHttpApis(Configuration);
该方法会：

添加 IHttpContextAccessor

添加 HttpContextHeaderFilter 为全局过滤器

扫描 所有 MasaApp.GetAssemblies() 中的 IExternalHttpApi 接口并注册

5. 使用
csharp
public class MyService
{
    private readonly IUserService _userService;

    public MyService(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<UserDto> GetUser(int id)
    {
        // 自动发起 HTTP 请求，无需手动处理 HttpClient
        return await _userService.GetUserAsync(id);
    }
}
高级用法
自定义配置键
使用 [ExternalHttpApiConfig] 特性覆盖默认的接口名 → 配置键转换规则：

csharp
[ExternalHttpApiConfig("LegacySystem")]
public interface IOldApi : IExternalHttpApi
{
    // 将从 ExternalApis:LegacySystem 节点读取 HttpHost 等配置
}
日志过滤器
ApiLoggingAttribute 可标注在接口或方法上，自动记录请求 URL 和响应状态码（使用 ILogger<ApiLoggingAttribute>）。

统一响应解包
如果你的外部 API 返回统一包装格式，例如：

json
{
  "code": 200,
  "message": "success",
  "data": { "id": 1, "name": "test" }
}
在方法上添加 [ApiReturnUnwrapper]，即可只得到 data 对应的对象，并且当 code != 200 时抛出异常（会被全局异常处理捕获）。

注意：ApiReturnUnwrapperAttribute 内部使用了 UserFriendlyException，你可以替换为自己的异常类型，或者引入你项目中的异常基类。

动态透传请求头
HttpContextHeaderFilter 是全局过滤器，会对 所有 标注了 [NeedAuthToken] 的接口，自动从当前 HttpContext 中提起以下头部并附加到出站请求：

X-Trace-Id = HttpContext.TraceIdentifier

Authorization = 原始请求的 Authorization 头

x-tenant-id = 原始请求的自定义头 __tenant__

若不需要全局透传，可自行调整注册逻辑为按需添加。

配置校验
启动时，若找不到 ExternalApis 配置节，或某个接口对应的 HttpHost 未配置/格式非法，将抛出 InvalidOperationException，阻止应用启动，确保问题及早暴露。

扩展与定制
所有过滤器均基于 IApiFilter / ApiFilterAttribute，你可以自由添加、修改。
如需替换程序集扫描方式（例如不使用 Masa），可在 ExternalHttpApiExtensions 中添加 Assembly 参数，或直接遍历特定程序集。