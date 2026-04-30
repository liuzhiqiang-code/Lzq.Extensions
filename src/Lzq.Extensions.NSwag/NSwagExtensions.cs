using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSwag;
using NSwag.AspNetCore;
using NSwag.Generation.Processors.Security;
using System.Text.Json;

namespace Lzq.Extensions.NSwag;

public static class NSwagExtensions
{
    public static IServiceCollection AddLzqNSwag(
        this IServiceCollection services,
        Action<NSwagOptions>? configureOptions = null)
    {
        var options = new NSwagOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(Options.Create(options));
        foreach (var docInfo in options.Documents)
        {
            // 仅为没有 ExternalUrl 的文档生成本地 swagger.json
            if (string.IsNullOrWhiteSpace(docInfo.ExternalUrl))
            {
                services.AddOpenApiDocument(doc =>
                {
                    doc.DocumentName = docInfo.Name;
                    doc.Title = docInfo.Title;
                    doc.Version = options.Version;
                    if (options.EnableJwtSecurity)
                    {
                        doc.AddSecurity(options.JwtSchemaName, Enumerable.Empty<string>(),
                            new OpenApiSecurityScheme
                            {
                                Type = OpenApiSecuritySchemeType.ApiKey,
                                Name = "Authorization",
                                In = OpenApiSecurityApiKeyLocation.Header,
                                Description = options.JwtDescription
                            });

                        doc.OperationProcessors.Add(
                            new AspNetCoreOperationSecurityScopeProcessor(options.JwtSchemaName));
                    }
                });
            }
        }

        return services;
    }

    public static void UseLzqNSwag(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<NSwagOptions>>().Value;

        app.UseOpenApi();

        if (options.EnableSwaggerUI)
        {
            // 如果启用密码保护，先添加密码验证中间件
            if (options.EnableSwaggerUIPassword)
            {
                app.UseSwaggerUIPasswordProtection(options);
            }

            app.UseSwaggerUi(settings =>
            {
                settings.Path = options.SwaggerBasePath ?? "/swagger";
                settings.SwaggerRoutes.Clear();

                foreach (var doc in options.Documents)
                {
                    if (!string.IsNullOrWhiteSpace(doc.ExternalUrl))
                    {
                        settings.SwaggerRoutes.Add(new SwaggerUiRoute(doc.Title, doc.ExternalUrl));
                    }
                    else
                    {
                        var url = $"/swagger/{doc.Name}/swagger.json";
                        settings.SwaggerRoutes.Add(new SwaggerUiRoute(doc.Title, url));
                    }
                }
            });
        }
    }

    /// <summary>
    /// 添加 Swagger UI 密码保护中间件
    /// </summary>
    private static void UseSwaggerUIPasswordProtection(
        this IApplicationBuilder app,
        NSwagOptions options)
    {
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Lzq.Extensions.NSwag.NSwagExtensions");

        app.Use(async (context, next) =>
        {
            try
            {
                var pathValue = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;
                var basePath = options.SwaggerBasePath ?? "/swagger";
                var verifyPath = options.SwaggerPasswordVerifyPath ?? "/swagger-password-verify";

                // 只对 Swagger UI 路径进行检查 —— 按字符串前缀匹配
                if (!pathValue.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pathValue, verifyPath, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Skipping non-swagger path: {Path}", pathValue);
                    await next.Invoke();
                    return;
                }

                // 检查 Cookie 验证
                if (context.Request.Cookies.TryGetValue(options.SwaggerUIPasswordCookieName, out var cookieValue))
                {
                    if (cookieValue == GeneratePasswordHash(options.SwaggerUIPassword))
                    {
                        logger.LogTrace("Swagger UI cookie validated for {Path}", pathValue);
                        await next.Invoke();
                        return;
                    }
                    logger.LogDebug("Swagger UI cookie present but invalid for {Path}", pathValue);
                }

                // 处理密码验证 POST
                if (context.Request.Method == HttpMethods.Post &&
                    pathValue.StartsWith(verifyPath, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Handling swagger password verification POST for {Path}", pathValue);
                    await HandlePasswordVerification(context, options);
                    return;
                }

                // 返回输入页面（精确匹配 base 页面）
                if (string.Equals(pathValue, basePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pathValue, basePath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Rendering swagger password page for {Path}", pathValue);
                    await RenderPasswordPage(context, options);
                    return;
                }

                // 其他 Swagger 路径也需要验证
                logger.LogWarning("Unauthorized swagger access to {Path}", pathValue);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("未授权");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Swagger UI password middleware");
                throw;
            }
        });
    }

    /// <summary>
    /// 处理密码验证请求
    /// </summary>
    private static async Task HandlePasswordVerification(HttpContext context, NSwagOptions options)
    {
        var form = await context.Request.ReadFormAsync();
        var password = form["password"].ToString();

        if (password == options.SwaggerUIPassword)
        {
            // 密码正确，设置 Cookie
            context.Response.Cookies.Append(
                options.SwaggerUIPasswordCookieName,
                GeneratePasswordHash(password),
                new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(options.SwaggerUIPasswordCookieExpirationMinutes)
                });

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            var successJson = JsonSerializer.Serialize(new { success = true });
            await context.Response.WriteAsync(successJson);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var failJson = JsonSerializer.Serialize(new { success = false, message = options.SwaggerUIPasswordErrorMessage });
            await context.Response.WriteAsync(failJson);
        }
    }

    /// <summary>
    /// 渲染密码输入页面
    /// </summary>
    private static async Task RenderPasswordPage(HttpContext context, NSwagOptions options)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Swagger UI - 身份验证</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
        }}
        .container {{
            background: white;
            border-radius: 8px;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
            padding: 40px;
            width: 100%;
            max-width: 400px;
        }}
        h1 {{
            text-align: center;
            margin-bottom: 10px;
            color: #333;
            font-size: 28px;
        }}
        p {{
            text-align: center;
            color: #666;
            margin-bottom: 30px;
            font-size: 14px;
        }}
        .form-group {{
            margin-bottom: 20px;
        }}
        label {{
            display: block;
            margin-bottom: 8px;
            color: #333;
            font-weight: 600;
            font-size: 14px;
        }}
        input[type='password'] {{
            width: 100%;
            padding: 12px;
            border: 2px solid #e0e0e0;
            border-radius: 4px;
            font-size: 14px;
            transition: border-color 0.3s;
        }}
        input[type='password']:focus {{
            outline: none;
            border-color: #667eea;
        }}
        button {{
            width: 100%;
            padding: 12px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            border-radius: 4px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            transition: transform 0.2s, box-shadow 0.2s;
        }}
        button:hover {{
            transform: translateY(-2px);
            box-shadow: 0 5px 15px rgba(102, 126, 234, 0.4);
        }}
        button:active {{
            transform: translateY(0);
        }}
        .error {{
            color: #d32f2f;
            font-size: 14px;
            margin-top: 10px;
            text-align: center;
            display: none;
        }}
        .loading {{
            display: none;
            text-align: center;
            color: #667eea;
            font-size: 14px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>🔐 API 文档</h1>
        <p>请输入访问密码</p>
        <form id='passwordForm' onsubmit='return handleSubmit(event)'>
            <div class='form-group'>
                <label for='password'>访问密码</label>
                <input 
                    type='password' 
                    id='password' 
                    name='password' 
                    placeholder='输入密码' 
                    required 
                    autofocus>
            </div>
            <button type='submit' id='submitBtn'>验证</button>
            <div class='error' id='error'></div>
            <div class='loading' id='loading'>验证中...</div>
        </form>
    </div>

    <script>
        function handleSubmit(event) {{
            event.preventDefault();
            const password = document.getElementById('password').value;
            const submitBtn = document.getElementById('submitBtn');
            const errorDiv = document.getElementById('error');
            const loadingDiv = document.getElementById('loading');

            submitBtn.disabled = true;
            loadingDiv.style.display = 'block';
            errorDiv.style.display = 'none';

            fetch('/swagger-password-verify', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/x-www-form-urlencoded' }},
                body: 'password=' + encodeURIComponent(password)
            }})
            .then(response => response.json())
            .then(data => {{
                if (data.success) {{
                    window.location.href = '/swagger/';
                }} else {{
                    errorDiv.textContent = data.message || '验证失败';
                    errorDiv.style.display = 'block';
                    document.getElementById('password').value = '';
                    submitBtn.disabled = false;
                    loadingDiv.style.display = 'none';
                }}
            }})
            .catch(err => {{
                errorDiv.textContent = '请求失败，请重试';
                errorDiv.style.display = 'block';
                submitBtn.disabled = false;
                loadingDiv.style.display = 'none';
            }});

            return false;
        }}
    </script>
</body>
</html>";

        await context.Response.WriteAsync(html);
    }

    /// <summary>
    /// 生成密码哈希值（简单实现）
    /// </summary>
    private static string GeneratePasswordHash(string password)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}
