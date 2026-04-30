# Lzq.Extensions.Jwt

基于 [ASP.NET](https://asp.net/) Core 的 JWT 认证扩展库，简化 Token 签发与用户身份读取。

## 快速上手

### 1. 配置 `appsettings.json`

json

```
{
  "Jwt": {
    "Issuer": "your-app",
    "Audience": "your-client",
    "SecurityKey": "your-secret-key-at-least-16-chars"
  }
}
```

### 2. 注册服务

csharp

```
builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    options.Issuer = "your-app";
    options.Audience = "your-app";
    options.SecurityKey = "your-secret-key-at-least-16-chars";
});
app.UseAuthentication();
app.UseAuthorization();
```

### 3. 生成 Token

csharp

```
public class AuthService
{
    private readonly IJwtService _jwtService;
    public AuthService(IJwtService jwtService) => _jwtService = jwtService;

    public TokenViewDto Login()
    {
        var user = new CurrentUser()
            .SetUserId("001")
            .SetUserName("demo")
            .SetRoles(new() { "admin" });
        return _jwtService.GenerateToken(user, TimeSpan.FromHours(2));
    }
}
```

### 4. 获取当前用户

csharp

```
[Authorize]
public class UserController : ControllerBase
{
    private readonly ICurrentUser _user;
    public UserController(ICurrentUser user) => _user = user;

    [HttpGet]
    public IActionResult Get() => Ok(new { _user.UserId, _user.UserName });
}
```

## 主要接口

| 接口/类 | 说明                                            |
| --------- | ------------------------------------------------- |
| `IJwtService`        | 生成 JWT Token                                  |
| `ICurrentUser`        | 获取当前请求用户信息（UserId、Roles、Email 等） |
| `JwtOption`        | 配置选项，对应配置文件 `Jwt` 节                      |

## 依赖项

- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `Masa.Utils.Security.Token`