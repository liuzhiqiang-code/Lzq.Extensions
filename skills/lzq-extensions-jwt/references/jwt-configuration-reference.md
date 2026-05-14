# JWT 配置参考 — `JwtOptions` 与认证中间件

本文档详细说明了 `Lzq.Extensions.Jwt` 库中 `JwtOptions` 的所有配置项、认证中间件的注册与验证流程，以及如何与 `AddLzqJwt` 方法协同工作。

## 1. JwtOptions 配置项

所有 JWT 相关配置通过 `Lzq.Extensions.Jwt.Options.JwtOptions` 类管理。可以在 `appsettings.json` 中设置，也可以直接在代码中通过 lambda 覆盖。

### 1.1 完整配置项列表

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Issuer` | `string` | - | 令牌签发者，验证 Token 时会比对 `iss` 声明。必须与 `AddLzqJwt` 中配置的一致。 |
| `Audience` | `string` | - | 令牌接收方，验证 Token 时会比对 `aud` 声明。通常设置为应用程序名。 |
| `SecurityKey` | `string` | `""` | 对称签名密钥，用于生成和验证 Token 的 HMAC-SHA256 签名。**必须至少 16 个字符**，生产环境应从环境变量或密钥管理器注入。 |
| `AccessExpiration` | `int` | `7200` (秒) | Access Token 的有效时长，默认为 2 小时。令牌过期后客户端需要重新申请。 |
| `RefreshSecret` | `string` | `""` | 刷新令牌专用的对称密钥，由 `Masa.Utils.Security.Token` 内部使用。若未设置，刷新功能不可用。 |
| `RefreshExpirationDays` | `int` | `72000` (约 2 年) | 刷新令牌的过期天数，用于控制刷新令牌的最大有效期。 |
| `RequireHttpsMetadata` | `bool` | `false` | 是否要求 HTTPS 连接。生产环境应设为 `true`，防止中间人攻击。 |
| `Authority` | `string` | - | 可选：若使用 OpenID Connect，指向身份提供者的 URL。当前实现中主要预留，一般不使用。 |

### 1.2 配置方式

#### 方式一：仅使用 `appsettings.json`

```json
{
  "Jwt": {
    "Issuer": "my-issuer",
    "Audience": "my-app",
    "SecurityKey": "super-secret-key-1234567890",
    "AccessExpiration": 7200
  }
}
```

注册时只需传入 `Configuration`：

``` csharp
builder.Services.AddLzqJwt(builder.Configuration);
```

#### 方式二：代码中直接覆盖

``` csharp
builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    options.Issuer = "custom-issuer";
    options.SecurityKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? "fallback-key";
    options.RequireHttpsMetadata = app.Environment.IsProduction();
});
```

代码覆盖的优先级高于配置文件，适合对敏感配置（如密钥）进行动态加载。

## 2. 认证中间件配置

`AddLzqJwt` 方法会调用 `AddAuthentication` 和 `AddJwtBearer`，并设置如下验证参数：

``` csharp
options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidIssuer = jwtOptions.Issuer,
    ValidateAudience = true,
    ValidAudience = jwtOptions.Audience,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecurityKey)),
    ClockSkew = TimeSpan.FromMinutes(5),
    RequireExpirationTime = true,
};
```

**验证行为：**

- **Issuer 验证**：`ValidateIssuer = true`，Token 中的 `iss` 必须等于 `ValidIssuer`。
- **Audience 验证**：`ValidateAudience = true`，Token 中的 `aud` 必须等于 `ValidAudience`。
- **生存期验证**：`ValidateLifetime = true`，Token 必须在有效期内（`nbf` 之后，`exp` 之前）。
- **签名验证**：`ValidateIssuerSigningKey = true`，使用配置的对称密钥验证 HMAC 签名。
- **时钟偏差**：`ClockSkew = TimeSpan.FromMinutes(5)`，允许服务端与客户端之间存在 5 分钟的时间偏差。

## 3. 授权策略

默认注册了两个授权策略：

| 策略名 | 要求                   |
| -------- | ------------------------ |
| `default`       | 仅要求用户已认证（`RequireAuthenticatedUser()`）   |
| `AdminOnly`       | 要求用户拥有 `Admin` 角色（`RequireRole("Admin")`） |

同时设置了一个**回退策略**（FallbackPolicy），所有未标明 `[AllowAnonymous]` 的端点都将要求认证。

``` csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("default", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

## 4. 依赖服务注册

`AddLzqJwt` 会自动注册以下服务：

| 服务 | 生命周期  | 说明                       |
| ------ | ----------- | ---------------------------- |
| `IJwtService` / `JwtService`  | Transient | Token 生成服务             |
| `ICurrentUser` / `CurrentUser`  | Transient | 当前用户读取服务           |
| `IHttpContextAccessor`     | Singleton | 用于在 `CurrentUser` 中获取 HTTP 上下文 |

## 5. 常见配置示例

### 开发环境最小配置

``` csharp
builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    options.Issuer = "dev";
    options.Audience = "dev";
    options.SecurityKey = "dev-secret-key-123456";
});
```

### 生产环境高安全配置

``` csharp
builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    options.Issuer = "api.mycompany.com";
    options.Audience = "web.mycompany.com";
    options.SecurityKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")!;
    options.RequireHttpsMetadata = true;
    options.AccessExpiration = 1800; // 30 分钟
});
```

## 6. 注意事项

- **密钥安全**：`SecurityKey` 绝对不能硬编码在源代码或配置文件中，务必使用环境变量、Azure Key Vault 等安全存储。
- **时钟同步**：若服务运行在不同机器上，确保所有节点的系统时间同步，否则可能导致 Token 验证失败。
- **撤销策略**：JWT 是无状态的，一旦签发无法主动撤销。如有即时撤销需求，可结合黑名单或使用短有效期 Token。
- **Claims 映射**：`CurrentUser` 从 Claims 中读取信息时，大小写敏感。生成 Token 时请严格使用 `UserId`、`UserName` 等驼峰命名。