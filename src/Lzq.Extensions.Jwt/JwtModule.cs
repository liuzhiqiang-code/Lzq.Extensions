using Lzq.Core;
using Lzq.Core.Interfaces;
using Lzq.Core.Modules;
using Lzq.Extensions.Jwt.Options;
using Lzq.Extensions.Jwt.Services;
using Masa.BuildingBlocks.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Lzq.Extensions.Jwt;

[DependsOn(typeof(CoreModule))]
public class JwtModule : BaseModule
{
    public override void Configure(ModuleConfigureContext context)
    {
        var currentAssembly = typeof(JwtModule).Assembly;
        MasaApp.TryAddAssemblies(currentAssembly);

        var services = context.Services;
        var configuration = context.Configuration;
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"));
    }

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var jwtOptions = context.ServiceProvider
            .GetRequiredService<IOptions<JwtOptions>>().Value;

        services.AddJwt(option =>
        {
            option.Issuer = jwtOptions.Issuer;
            option.Audience = jwtOptions.Audience;
            option.SecurityKey = jwtOptions.SecurityKey;
        });

        // 注册服务
        services.AddHttpContextAccessor();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // 使用 IOptions<JwtOptions> 配置 JwtBearer
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                ConfigureJwtBearerOptions(options, jwtOptions);
            });

        // 网关一般的授权策略是只过滤掉不合法身份认证的请求，更详细的授权在服务中认证
        services.AddAuthorization(options =>
        {
            options.AddPolicy("default", policy => policy.RequireAuthenticatedUser());
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));

            // 退回策略
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
    }

    public override void OnPostApplicationInitialization(ModuleInitContext context)
    {
        context.Logger.LogInformation("UseAuthentication");
        context.App.UseAuthentication();
        context.App.UseAuthorization();
    }

    private static void ConfigureJwtBearerOptions(JwtBearerOptions options, JwtOptions jwtOptions)
    {
        options.Authority = jwtOptions.Authority;
        options.RequireHttpsMetadata = jwtOptions.RequireHttpsMetadata;
        options.Audience = jwtOptions.Audience;

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
    }
}