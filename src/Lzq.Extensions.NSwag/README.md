# Lzq.Extensions.NSwag

`Lzq.Extensions.NSwag` 是一个轻量级的 NSwag 封装库，旨在简化 .NET 10 应用中的 OpenAPI 文档配置。它不仅支持快速注册本地文档，还特别优化了**微服务网关场景下的 Swagger 聚合**与**环境隔离的访问保护**。

## 🌟 核心特性

- 🚀 **极简注册**：通过 Lambda 配置即可完成多文档、JWT 安全定义等配置。
- 🌐 **聚合支持**：轻松集成外部服务的 `swagger.json` 到当前网关 UI。
- 🔒 **内置保护**：为内网或测试环境提供基于 Cookie 的简单 UI 密码访问控制。
- 🛠️ **高度兼容**：深度集成 NSwag 原生能力，不破坏底层配置灵活性。

---

## 📦 快速开始

### 1. 注册服务 (构建期)

在 `Program.cs` 中添加配置：

``` C#
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLzqNSwag(options => 
{
    options.Title = "Lzq 企业级网关 API";
    options.Version = "v1.0";
    options.EnableJwtSecurity = true; // 自动添加 Bearer 授权按钮

    // 配置多文档
    options.Documents = new List<SwaggerDocumentInfo>
    {
        // 1. 本地生成：访问路径 /swagger/local-api/swagger.json
        new() { Name = "local-api", Title = "本地业务接口" }, 
        
        // 2. 外部聚合：直接引用微服务节点
        new() 
        { 
            Name = "order-svc", 
            Title = "订单微服务", 
            ExternalUrl = "https://order-api.internal/swagger/v1/swagger.json" 
        }
    };

    // 访问保护：非开发环境强制启用 UI 密码
    if (!builder.Environment.IsDevelopment())
    {
        options.EnableSwaggerUIPassword = true;
        options.SwaggerUIPassword = "YourComplexPassword";
    }
});
```

### 2. 启用中间件 (运行期)

``` C#
var app = builder.Build();

// 启用 NSwag 路由与 UI（默认路径: /swagger）
app.UseLzqNSwag();

app.Run();
```

---

## ⚙️ 配置项详解 (`NSwagOptions`)

| **配置项** | **类型** | **默认值** | **说明**                                    |
| -- | -- | -- | ------------------------------------- |
| `Title` | `string` | `"API Documentation"` | UI 页面左上角显示的标题             |
| `Version` | `string` | `"1.0.0"` | API 文档版本号                      |
| `EnableSwaggerUI` | `bool` | `true` | 是否开启 Swagger UI 交互页面        |
| `EnableJwtSecurity` | `bool` | `true` | 是否在 UI 中开启 JWT 授权配置锁图标 |
| `EnableSwaggerUIPassword` | `bool` | `false` | 是否开启 UI 访问密码保护            |
| `SwaggerUIPassword` | `string` | `"admin123"` | UI 验证密码 (建议通过环境变量配置)  |
| `SwaggerBasePath` | `string` | `"/swagger"` | Swagger UI 的访问基路径             |
| `Documents` | `List` | `v1` | 文档集合，包含`Name`,`Title`,`ExternalUrl`                    |