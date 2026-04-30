using Lzq.Core.Interfaces;
using Lzq.Extensions.Jwt.Options;
using Lzq.Extensions.Jwt.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Lzq.Extensions.Jwt;

public static class JwtExtensions
{
    public static IServiceCollection AddLzqJwt(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtOptions>? configureOptions = null)
    {
        // 注册 Options（支持配置文件 + 代码配置）
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Configure(configureOptions ?? (_ => { }));

        var jwtOptions = services.BuildServiceProvider()
                    .GetRequiredService<IOptions<JwtOptions>>().Value;

        services.AddJwt(option =>
        {
            option.Issuer = jwtOptions.Issuer;
            option.Audience = jwtOptions.Audience;
            option.SecurityKey = jwtOptions.SecurityKey;
        });

        // 注册服务
        services.AddHttpContextAccessor();
        services.AddTransient<IJwtService, JwtService>();
        services.AddTransient<ICurrentUser, CurrentUser>();

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

            // 退回策略
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
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

