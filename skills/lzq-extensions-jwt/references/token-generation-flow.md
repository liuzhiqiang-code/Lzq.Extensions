# Token 生成与验证流程

本文档描述了 `Lzq.Extensions.Jwt` 库中 JWT Token 的生成、签发、验证以及 `ICurrentUser` 读取的完整内部流程。

## 1. Token 生成流程

Token 生成由 `JwtService.GenerateToken(ICurrentUser user, TimeSpan timeSpan)` 方法完成。内部调用了 `Masa.Utils.Security.Token.JwtUtils.CreateToken`。

### 步骤

1. **构建 Claims**  
   从传入的 `ICurrentUser` 实例中提取以下信息，封装为 Claim 数组：
   - `UserId`
   - `UserName`
   - `Roles`（序列化为 JSON 字符串）
   - `Email`
   - `Sex`
   - 当前时间 `datetime`（格式 `yyyy-MM-dd HH:mm:ss`）
   - `token_type` 固定为 `"access"`
   - `TenantId`
   - `Sid`（Token 唯一标识）

2. **调用 JwtUtils.CreateToken**  
   传入 Claims 数组和过期时长 `timeSpan`，使用配置的 `SecurityKey` 对 Token 进行 HMAC-SHA256 签名。

3. **返回 TokenViewDto**  
   构造并返回 `TokenViewDto` 对象：
   ```csharp
   return new TokenViewDto
   {
       AccessToken = accessToken,
       TokenType = "Bearer",
       ExpiresIn = (int)timeSpan.TotalSeconds,
   };
   ```


### 示例代码

``` csharp
public TokenViewDto GenerateToken(ICurrentUser user, TimeSpan timeSpan)
{
    var claims = new[]
    {
        new Claim("UserId", user.UserId),
        new Claim("UserName", user.UserName ?? ""),
        new Claim("Roles", JsonSerializer.Serialize(user.Roles)),
        new Claim("Email", user.Email ?? ""),
        new Claim("Sex", user.Sex),
        new Claim("datetime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
        new Claim("token_type", "access"),
        new Claim("TenantId", user.TenantId),
        new Claim(JwtRegisteredClaimNames.Sid, user.Sid),
    };
    var accessToken = JwtUtils.CreateToken(claims, timeSpan);
    return new TokenViewDto
    {
        AccessToken = accessToken,
        TokenType = "Bearer",
        ExpiresIn = (int)timeSpan.TotalSeconds,
    };
}
```

## 2. Token 验证流程

Token 验证由 [ASP.NET](https://asp.net/) Core 中间件 `JwtBearerHandler` 自动完成，配置在 `AddLzqJwt` 中的 `TokenValidationParameters` 控制。

### 验证步骤

1. **提取 Token**
    从 HTTP 请求头 `Authorization: Bearer <token>` 中提取 JWT 字符串。
2. **验证签名**
    使用配置的 `SecurityKey` 计算 HMAC-SHA256 签名，比对 Token 中的签名是否一致。
3. **验证声明**

    - 检查 `iss` 是否与 `ValidIssuer` 匹配。
    - 检查 `aud` 是否与 `ValidAudience` 匹配。
    - 检查 `exp` 是否未过期（允许 5 分钟时钟偏移）。
    - 检查 `nbf` 是否已生效。
4. **构造 ClaimsPrincipal**
    验证通过后，[ASP.NET](https://asp.net/) Core 会将 Claims 填充到 `HttpContext.User` 中。

## 3. CurrentUser 读取流程

`CurrentUser` 实现了 `ICurrentUser` 接口，通过 `IHttpContextAccessor` 获取当前请求的 `ClaimsPrincipal`，然后从 Claims 中提取用户信息。

### 读取优先级

每个属性按以下优先级取值：

1. 从 `HttpContext.User.Claims` 中查找对应类型的 Claim。
2. 如果未找到，返回手动设置的值（通过 `Set*` 方法赋的值）。
3. 如果仍未找到，返回默认值（如 `string.Empty`）。

### 示例

``` csharp
public string UserId => FindClaimStringValue("UserId") ?? _userId ?? string.Empty;
```

其中 `FindClaimStringValue` 从 Claims 中查找，`_userId` 是手动设置的字段。

### 手动设置

在非 HTTP 请求场景（如后台任务、控制台应用）中，可通过链式调用手动构建 `CurrentUser` 实例：

``` csharp
var user = new CurrentUser()
    .SetUserId("001")
    .SetUserName("admin")
    .SetRoles(new List<string> { "Admin" });
```

## 4. 整体交互图

## 5. 注意事项

- **Token 过期**：`exp` 声明是绝对时间，客户端需在过期前刷新 Token。当前库未提供开箱即用的刷新逻辑，可结合 `RefreshSecret` 自定义实现。
- **Claims 大小写**：`CurrentUser` 读取时使用精确的 Claim 类型名称（如 `"UserId"`），生成 Token 时必须保持一致。
- **多租户**：`TenantId` 被注入到 Token 中，可在多租户应用中用于数据隔离。