---
name: lzq-extensions-jwt
description: Integrate JWT authentication into ASP.NET Core applications using Lzq.Extensions.Jwt. Use when generating access tokens, protecting APIs with JWT, and reading current user information from claims. Covers token generation, configuration, and current user access.
license: Proprietary
compatibility: Requires .NET 8+, Lzq.Extensions.Jwt NuGet package, and Lzq.Core
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.Jwt` 是一个基于 ASP.NET Core 的 JWT 认证扩展库，提供了开箱即用的 Token 签发、用户身份读取以及认证中间件注册功能。它封装了 `Microsoft.AspNetCore.Authentication.JwtBearer` 和 `Masa.Utils.Security.Token`，使开发者能够用最少的代码实现完整的 JWT 认证流程。

在以下场景使用本技能：
- 需要为 Web API 或微服务添加 JWT 身份认证
- 需要快速生成 Access Token 并下发给客户端
- 需要在控制器中方便地获取当前登录用户的 ID、角色、邮箱等信息
- 需要集中管理 JWT 的签发密钥、过期时间等配置

## 何时使用

- 用户要求“添加 JWT 认证”或“实现登录接口并返回 Token”
- 用户提到 `Lzq.Extensions.Jwt`、`AddLzqJwt`、`IJwtService` 或 `ICurrentUser`
- 需要将用户信息编码到 JWT 中并在请求中传递
- 需要保护 API 端点，只允许已认证用户访问
- 需要从 Token 中解析当前用户信息

## 何时不使用

- 纯前端任务
- 采用其他认证方案（如 OAuth2、Cookie 认证等）
- 与 JWT 或 .NET 配置无关的任务

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.Jwt
```

### 2. 配置 `appsettings.json`

在 `appsettings.json` 中添加 `Jwt` 配置节，至少需要提供 `Issuer`、`Audience` 和 `SecurityKey`。

``` json
{
  "Jwt": {
    "Issuer": "your-app",
    "Audience": "your-client",
    "SecurityKey": "your-secret-key-at-least-16-chars"
  }
}
```

`SecurityKey` 必须是长度至少 16 个字符的字符串，用于签名 Token。

### 3. 注册服务

在 `Program.cs` 中调用 `AddLzqJwt` 并传入配置：

``` csharp
using Lzq.Extensions.Jwt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    // 可在此处覆盖配置
    options.Issuer = "my-app";
    options.Audience = "my-app";
    options.SecurityKey = "a-very-secure-key-123456";
});

var app = builder.Build();

// 启用认证和授权中间件
app.UseAuthentication();
app.UseAuthorization();

app.Run();
```

### 4. 生成 Token

在登录逻辑中通过 `IJwtService` 签发 Token：

``` csharp
using Lzq.Extensions.Jwt.Services;
using Lzq.Extensions.Jwt.Models;
using Lzq.Extensions.Jwt;

public class AuthService
{
    private readonly IJwtService _jwtService;
    public AuthService(IJwtService jwtService) => _jwtService = jwtService;

    public TokenViewDto Login(string userId, string userName, List<string> roles)
    {
        var user = new CurrentUser()
            .SetUserId(userId)
            .SetUserName(userName)
            .SetRoles(roles)
            .SetEmail("user@example.com")
            .SetTenantId("tenant-001");

        // 生成一个有效期为 2 小时的 Token
        return _jwtService.GenerateToken(user, TimeSpan.FromHours(2));
    }
}
```

Token 返回对象 `TokenViewDto` 包含 `access_token`（JWT 字符串）、`token_type`（固定为 `"Bearer"`）和 `expires_in`（过期时间秒数）。

### 5. 保护 API

在需要认证的控制器或最小 API 上添加 `[Authorize]` 特性：

``` csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly ICurrentUser _currentUser;

    public UserController(ICurrentUser currentUser) => _currentUser = currentUser;

    [HttpGet("profile")]
    public IActionResult GetProfile()
    {
        return Ok(new
        {
            _currentUser.UserId,
            _currentUser.UserName,
            _currentUser.Roles,
            _currentUser.Email,
            _currentUser.TenantId
        });
    }
}
```

### 6. 获取当前用户

在任何服务或控制器中注入 `ICurrentUser` 即可读取当前请求的用户信息。`ICurrentUser` 的实现会从 `HttpContext.User` 的 Claims 中提取数据。

若需要在非 HTTP 请求环境中使用（如后台任务），也可以手动构建 `CurrentUser` 实例：

``` csharp
var manualUser = new CurrentUser()
    .SetUserId("999")
    .SetUserName("bot");
```

## 配置参考 (`JwtOptions`)

| 配置项 | 类型 | 默认值 | 说明                                           |
| -------- | ------ | -------- | ------------------------------------------------ |
| `Issuer`       | `string`     | -      | Token 签发者，验证时用于 `ValidIssuer`                      |
| `Audience`       | `string`     | -      | Token 接收方，验证时用于 `ValidAudience`                      |
| `SecurityKey`       | `string`     | `""`       | 对称签名密钥，至少 16 个字符                   |
| `AccessExpiration`       | `int`     | `7200` (秒)  | Access Token 过期时间                          |
| `RefreshSecret`       | `string`     | `""`       | 刷新令牌的密钥（目前由 `Masa.Utils.Security.Token` 使用）                 |
| `RefreshExpirationDays`       | `int`     | `72000`       | 刷新令牌过期天数                               |
| `RequireHttpsMetadata`       | `bool`     | `false`       | 是否要求 HTTPS；生产环境建议设为 `true`              |
| `Authority`       | `string`     | -      | 可选：使用 OpenID Connect 时指定授权服务器地址 |

## 令牌验证参数

`AddLzqJwt` 配置的 `TokenValidationParameters` 具有以下默认行为：

- 验证 Issuer (`ValidateIssuer = true`)
- 验证 Audience (`ValidateAudience = true`)
- 验证生存期 (`ValidateLifetime = true`)
- 验证签名密钥 (`ValidateIssuerSigningKey = true`)
- 时钟偏差：5 分钟 (`ClockSkew = TimeSpan.FromMinutes(5)`)

## 常见问题

- **Token 验证失败 (401)** 
  检查客户端是否在请求头中携带了 `Authorization: Bearer <token>`，以及 Token 是否已过期。同时确认 `SecurityKey` 与签发时一致。
- **`ICurrentUser`** **中的字段为空**
  确保 Token 生成时使用了 `CurrentUser` 的相应 Set 方法，且字段已正确添加到 Claims 中。接收方必须使用相同的 `SecurityKey` 进行验证。
- **如何自定义 Claims**
  目前 `JwtService.GenerateToken` 会生成固定的 Claims（UserId, UserName, Roles, Email, Sex, TenantId, Sid, datetime）。如需扩展，可自行实现 `IJwtService` 或修改现有实现。
- **刷新 Token 的处理**
  `Masa.Utils.Security.Token` 提供了刷新 Token 的能力，但当前示例中并未暴露刷新端点。你可以参考 `RefreshSecret` 配置项，利用 `JwtUtils.CreateToken` 生成刷新 Token 并存储。

## 核心类型

| 类型 | 说明                           |
| ------ | -------------------------------- |
| `IJwtService`     | Token 生成服务，提供 `GenerateToken` 方法     |
| `ICurrentUser`     | 当前用户信息接口，包含 `UserId`, `UserName`, `Roles` 等 |
| `JwtOptions`     | 配置选项，对应 `appsettings.json` 中的 `Jwt` 节       |
| `TokenViewDto`     | Token 响应对象，包含 `access_token`, `token_type`, `expires_in`      |

## 参考资料

详细实现可参阅：

- `references/jwt-configuration-reference.md` — `JwtOptions` 与认证中间件的详细配置
- `references/token-generation-flow.md` — Token 生成及验证的内部流程说明