namespace Lzq.Extensions.NSwag;

public class NSwagOptions
{
    public string Title { get; set; } = "API Documentation";
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public bool EnableSwaggerUI { get; set; } = true;
    public bool EnableJwtSecurity { get; set; } = true;
    public string JwtSchemaName { get; set; } = "JWT";
    public string JwtDescription { get; set; } = "请输入 Token，格式为: Bearer {your_token}";


    /// <summary>
    /// 是否启用 Swagger UI 密码保护
    /// </summary>
    public bool EnableSwaggerUIPassword { get; set; } = false;

    /// <summary>
    /// Swagger UI 访问密码（仅当 EnableSwaggerUIPassword 为 true 时生效）
    /// </summary>
    public string SwaggerUIPassword { get; set; } = "admin123";

    /// <summary>
    /// 密码验证失败时显示的消息
    /// </summary>
    public string SwaggerUIPasswordErrorMessage { get; set; } = "密码错误，无法访问 Swagger UI";

    /// <summary>
    /// 密码验证 Cookie 名称（用于记住已验证状态）
    /// </summary>
    public string SwaggerUIPasswordCookieName { get; set; } = "SwaggerUIAuth";

    /// <summary>
    /// 密码验证 Cookie 过期时间（分钟）
    /// </summary>
    public int SwaggerUIPasswordCookieExpirationMinutes { get; set; } = 480; // 8小时

    public string SwaggerBasePath { get; set; } = "/swagger";
    public string SwaggerPasswordVerifyPath { get; set; } = "/swagger-password-verify";

    public List<SwaggerDocumentInfo> Documents { get; set; } = new()
    {
        new SwaggerDocumentInfo { Name = "v1", Title = "API V1" }
    };
}

public class SwaggerDocumentInfo
{
    /// <summary>文档标识，会用于路径 /swagger/{Name}/swagger.json</summary>
    public string Name { get; set; } = "v1";

    /// <summary>在 UI 中显示的标题</summary>
    public string Title { get; set; } = "API";

    /// <summary>
    /// 可选：外部 OpenAPI JSON 的完整 Url（若设置则不会在本应用注册 document，而直接将外部 URL 暴露到 UI）
    /// </summary>
    public string? ExternalUrl { get; set; }
}