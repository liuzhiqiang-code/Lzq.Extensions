---
name: lzq-extensions-nswag
description: 使用 Lzq.Extensions.NSwag 库配置 OpenAPI 文档及 Swagger UI。适用于在 .NET 项目中快速搭建 NSwag、启用 JWT 安全认证、聚合外部微服务的 swagger 文档，或为 Swagger UI 添加密码保护。
license: Proprietary
compatibility: 需要 .NET 8+ 和 Lzq.Extensions.NSwag NuGet 包
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.NSwag` 是一个轻量级的 NSwag 封装库，旨在简化 ASP.NET Core 应用中的 OpenAPI 文档配置。它通过流畅的 API 支持多文档（本地/远程）注册、JWT 安全定义，以及内置的 Swagger UI 密码保护。

在以下场景使用本技能：
- 需要快速为 .NET 项目添加 Swagger/OpenAPI
- 需要在单一 Swagger UI 中聚合多个下游服务的 `swagger.json`
- 需要为非开发环境的 Swagger UI 添加密码保护
- 需要在 Swagger 中为 API 端点配置 JWT 授权

## 何时使用

- 用户要求“添加 Swagger”或“配置 NSwag”
- 用户提到 `Lzq.Extensions.NSwag`、`AddLzqNSwag` 或 `UseLzqNSwag`
- 用户希望将外部 API 文档聚合到网关
- 用户需要为 Swagger UI 添加密码保护
- 用户询问 Swagger 的 JWT 安全配置或多文档注册

## 何时不使用

- 与 API 文档或 .NET 配置无关的任务
- 使用其他 OpenAPI 库（如 Swashbuckle）的项目
- 前端任务

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.NSwag
```

### 2. 在 `Program.cs` 中注册服务

``` csharp
using Lzq.Extensions.NSwag;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLzqNSwag(options =>
{
    // 基本信息
    options.Title = "我的 API";
    options.Version = "v1";
    options.EnableJwtSecurity = true;   // 在 Swagger UI 中添加 Bearer 授权按钮

    // 配置文档（本地和/或远程）
    options.Documents = new List<SwaggerDocumentInfo>
    {
        // 本地文档 —— 自动生成
        new SwaggerDocumentInfo { Name = "v1", Title = "API V1" },
        // 外部文档 —— 从远程微服务获取
        new SwaggerDocumentInfo
        {
            Name = "orders",
            Title = "订单服务",
            ExternalUrl = "https://orders-api.internal/swagger/v1/swagger.json"
        }
    };

    // 保护 Swagger UI（建议非开发环境启用）
    if (!builder.Environment.IsDevelopment())
    {
        options.EnableSwaggerUIPassword = true;
        options.SwaggerUIPassword = Environment.GetEnvironmentVariable("SWAGGER_PASSWORD") ?? "changeme";
    }
});
```

### 3. 启用中间件

``` csharp
var app = builder.Build();

app.UseLzqNSwag();   // 添加 OpenAPI/Swagger 端点及可选的密码保护
```

默认 Swagger UI 地址为 `/swagger`。

## 配置参考

| 配置项 | 说明                                   |
| -------- | ---------------------------------------- |
| `Title`       | 显示在 Swagger UI 头部的标题           |
| `Version`       | 文档版本号（例如 `"1.0.0"`）                    |
| `EnableSwaggerUI`       | 设为 `false` 可禁用 UI（仍提供 `swagger.json`）             |
| `EnableJwtSecurity`       | 添加带有 Bearer 令牌输入的“授权”按钮 |
| `JwtSchemaName`       | 安全方案名称（默认 `"JWT"`）                  |
| `JwtDescription`       | 在授权弹出窗口中显示的提示文本         |
| `EnableSwaggerUIPassword`       | 启用密码表单保护 UI                    |
| `SwaggerUIPassword`       | 明文密码（应通过环境变量或机密存储）   |
| `SwaggerUIPasswordCookieName`       | 用于持久化认证的 Cookie 名称（默认 `"SwaggerUIAuth"`）  |
| `SwaggerUIPasswordCookieExpirationMinutes`       | Cookie 有效时长（默认 `480` 分钟）          |
| `SwaggerBasePath`       | Swagger UI 的访问路径（默认 `"/swagger"`）         |
| `SwaggerPasswordVerifyPath`       | 密码验证的端点（默认 `"/swagger-password-verify"`）                |
| `Documents`       | `SwaggerDocumentInfo` 对象列表（见下文）                    |

### 文档配置 (`SwaggerDocumentInfo`)

| 属性 | 必填 | 说明                                     |
| ------ | ------ | ------------------------------------------ |
| `Name`     | 是   | 文档标识，用于 URL 路径 `/swagger/{Name}/swagger.json`                 |
| `Title`     | 是   | 在 Swagger UI 下拉列表中的显示标题       |
| `ExternalUrl`     | 否   | 外部 `swagger.json` 的完整 URL；设置后不会生成本地文档 |

## 密码保护

当 `EnableSwaggerUIPassword` 设为 `true` 时：

- 访问 `/swagger`（或自定义 `SwaggerBasePath`）会显示密码表单。
- 成功 POST 到 `/swagger-password-verify` 后，会设置一个签名 Cookie。
- 后续携带有效 Cookie 的请求将绕过密码表单。
- 密码使用 SHA‑256 哈希后与 Cookie 比对。

可通过 `SwaggerUIPasswordErrorMessage` 自定义错误消息。

## JWT 安全

当 `EnableJwtSecurity` 设为 `true` 时：

- NSwag 自动为生成的 OpenAPI 文档添加 `"JWT"` 安全方案。
- Swagger UI 中出现“授权”按钮。
- 用户可粘贴令牌（无需 `Bearer` 前缀），请求头将自动携带 `Authorization: Bearer <token>`。

## 外部文档聚合

要纳入其他微服务的 API，添加带有 `ExternalUrl` 的 `SwaggerDocumentInfo`。库**不会**生成本地文档，而是直接在 Swagger UI 中引用远程 URL。

``` csharp
new SwaggerDocumentInfo
{
    Name = "external-service",
    Title = "外部 API",
    ExternalUrl = "https://remote-api/swagger/v1/swagger.json"
}
```

多个外部文档可一并加入 `Documents` 列表。

## 常见问题

-  **"No service for type 'Microsoft.Extensions.Options.IOptions<NSwagOptions>' has been registered."** 
  请确保在构建应用前调用了 `builder.Services.AddLzqNSwag(...)`。
- **密码保护未生效**
  检查 Cookie 路径是否与基础路径匹配；两者默认均为 `/swagger`。若修改了 `SwaggerBasePath`，请相应调整相关路径。
- **外部文档未显示**
  确认远端服务器可达且未被 CORS 阻止。Swagger UI 从浏览器端获取外部 JSON，而非服务器端。
- **JWT 安全未生效**
  确认在构建应用前设置了 `EnableJwtSecurity = true`。重新构建并检查 Swagger JSON 端点是否包含 `"JWT"` 安全方案。

## 参考资料

详细配置示例见配套参考文件：

- `references/nswag-options-reference.md` — 每个 `NSwagOptions` 字段的完整说明
- `references/password-protection-flow.md` — 密码中间件的内部实现说明