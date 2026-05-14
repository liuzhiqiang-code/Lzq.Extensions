# NSwag 配置项详解 (`NSwagOptions`)

本文档详细说明了 `Lzq.Extensions.NSwag` 库中 `NSwagOptions` 类的每一项配置，帮助你灵活控制 OpenAPI 文档生成、Swagger UI 行为以及安全访问。

## 基础信息

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Title` | `string` | `"API Documentation"` | Swagger UI 页面左上角显示的标题。 |
| `Description` | `string` | `""` | 对 API 文档的简单描述，会显示在 Swagger UI 的标题下方。 |
| `Version` | `string` | `"1.0.0"` | 文档版本号，通常与你的 API 版本保持一致。 |

## Swagger UI 控制

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `EnableSwaggerUI` | `bool` | `true` | 是否启用 Swagger UI 交互页面。如果设为 `false`，仍可通过 `/swagger/v1/swagger.json` 获取原始 JSON 文档，但不会渲染可视化界面。 |
| `SwaggerBasePath` | `string` | `"/swagger"` | Swagger UI 的访问基路径。例如设为 `"/api-docs"` 后，UI 地址变为 `/api-docs`。 |

## 安全配置

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `EnableJwtSecurity` | `bool` | `true` | 是否在 Swagger UI 中启用 JWT 授权功能。开启后，Swagger 会自动添加一个“授权”按钮，用户可以输入 Bearer Token 对受保护的接口进行测试。 |
| `JwtSchemaName` | `string` | `"JWT"` | JWT 安全方案的名称，会显示在生成的 OpenAPI JSON 中。 |
| `JwtDescription` | `string` | `"请输入 Token，格式为: Bearer {your_token}"` | 在 Swagger UI 授权弹窗中显示的提示文字，指导用户如何输入 Token。 |

## Swagger UI 密码保护

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `EnableSwaggerUIPassword` | `bool` | `false` | 是否启用 Swagger UI 的密码访问保护。建议在非开发环境（测试、预发布、生产）中开启，防止接口文档被未授权访问。 |
| `SwaggerUIPassword` | `string` | `"admin123"` | 访问 Swagger UI 所需的明文密码。**强烈建议**通过环境变量或配置中心注入，避免硬编码。 |
| `SwaggerUIPasswordErrorMessage` | `string` | `"密码错误，无法访问 Swagger UI"` | 当用户输入错误密码时，页面上显示的错误提示信息。 |
| `SwaggerUIPasswordCookieName` | `string` | `"SwaggerUIAuth"` | 用于记录已验证状态的 Cookie 名称。浏览器在密码验证通过后会存储该 Cookie，下次访问时无需重复输入。 |
| `SwaggerUIPasswordCookieExpirationMinutes` | `int` | `480` (8小时) | 验证 Cookie 的有效时长，单位分钟。过期后用户需要重新输入密码。 |
| `SwaggerPasswordVerifyPath` | `string` | `"/swagger-password-verify"` | 密码验证接口的路径。当用户提交密码表单时，前端会向该地址发送 POST 请求。 |

## 文档集合

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Documents` | `List<SwaggerDocumentInfo>` | 包含一个默认的 `v1` 文档 | 需要暴露的文档列表。你可以添加多个文档，每个文档可以是本地生成或从外部 URL 引入的。 |

### `SwaggerDocumentInfo` 成员

| 属性 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `Name` | `string` | **是** | 文档的唯一标识。对应访问路径为 `/swagger/{Name}/swagger.json`。 |
| `Title` | `string` | **是** | 在 Swagger UI 右上角下拉菜单中显示的文档标题，方便用户区分不同服务。 |
| `ExternalUrl` | `string?` | 否 | 如果提供该值，则不会在本应用中生成该文档的 `swagger.json`，而是直接引用外部 URL。常用于微服务网关聚合下游服务的文档。示例：`"https://order-service/swagger/v1/swagger.json"`。 |

## 典型配置示例

### 最小配置（仅本地文档）

```csharp
builder.Services.AddLzqNSwag(options =>
{
    options.Title = "我的 API";
});
```

### 带 JWT 和密码保护

``` csharp
builder.Services.AddLzqNSwag(options =>
{
    options.Title = "安全 API";
    options.EnableJwtSecurity = true;
    options.EnableSwaggerUIPassword = true;
    options.SwaggerUIPassword = Configuration["Swagger:Password"];
});
```

### 聚合多个外部微服务

``` csharp
builder.Services.AddLzqNSwag(options =>
{
    options.Title = "统一网关 API";
    options.Documents = new List<SwaggerDocumentInfo>
    {
        new() { Name = "gateway", Title = "网关接口" },
        new() { Name = "user-service", Title = "用户服务", ExternalUrl = "https://user-api/swagger/v1/swagger.json" },
        new() { Name = "order-service", Title = "订单服务", ExternalUrl = "https://order-api/swagger/v1/swagger.json" }
    };
});
```