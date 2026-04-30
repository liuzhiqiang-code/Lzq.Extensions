using Lzq.Extensions.WebApiClientCore.Aop;
using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Lzq.Extensions.WebApiClientCore;

public static class ExternalHttpApiExtensions
{
    /// <summary>
    /// 扫描程序集中所有继承自 IExternalHttpApi 的接口，自动注册到 DI 容器。
    /// 如果某个接口在配置文件中找不到对应的 HttpHost（基地址），则抛出异常。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">IConfiguration 实例</param>
    /// <param name="assemblyToScan">要扫描的程序集，默认为调用方程序集</param>
    /// <returns></returns>
    public static IServiceCollection AddExternalHttpApis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<HttpContextHeaderFilter>();

        var assembliesToScan = MasaApp.GetAssemblies();

        // 1. 找出所有实现 IExternalHttpApi 的接口
        var apiInterfaces = assembliesToScan
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && !t.IsGenericTypeDefinition && typeof(IExternalHttpApi).IsAssignableFrom(t))
            .ToList();

        if (!apiInterfaces.Any())
            return services;

        // 2. 获取配置根节点（约定所有外部 API 配置都在 "ExternalApis" 节下）
        var externalApisSection = configuration.GetSection("ExternalApis");
        if (!externalApisSection.Exists())
        {
            throw new InvalidOperationException(
                "Missing configuration section 'ExternalApis'. Please add it in appsettings.json.");
        }

        // 3. 逐个接口注册
        foreach (var apiType in apiInterfaces)
        {
            // 获取配置键：接口名（去除首字母 I）
            var configKey = GetConfigKey(apiType);
            var apiConfigSection = externalApisSection.GetSection(configKey);

            // 验证配置是否存在 HttpHost
            var httpHost = apiConfigSection["HttpHost"];
            if (string.IsNullOrWhiteSpace(httpHost))
            {
                throw new InvalidOperationException(
                    $"Missing 'HttpHost' configuration for API interface '{apiType.Name}' in section 'ExternalApis:{configKey}'. " +
                    $"Please verify your appsettings.json.");
            }

            // 验证 URL 格式
            httpHost = httpHost.Trim();
            if (!Uri.TryCreate(httpHost, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Invalid 'HttpHost' URL format '{httpHost}' for API interface '{apiType.Name}'. " +
                    $"The URL must be a valid absolute HTTP or HTTPS URL.");
            }

            services.AddHttpApi(apiType, (options, provider) =>
            {
                options.HttpHost = new Uri(httpHost.TrimEnd('/'));

                // 动态从注入容器获取 Header 过滤器
                var headerFilter = provider.GetRequiredService<HttpContextHeaderFilter>();
                options.GlobalFilters.Add(headerFilter);
            }).ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(apiConfigSection["Timeout"] != null ? double.Parse(apiConfigSection["Timeout"]) : 100)); ;
        }

        return services;
    }

    private static string GetConfigKey(Type apiInterfaceType)
    {
        // 尝试从自定义特性获取配置键（可选扩展）
        var attr = apiInterfaceType.GetCustomAttribute<ExternalHttpApiConfigAttribute>();
        if (attr != null && !string.IsNullOrEmpty(attr.ConfigKey))
            return attr.ConfigKey;

        // 默认规则：接口名去掉首字母 I
        var name = apiInterfaceType.Name;
        if (name.StartsWith("I") && char.IsUpper(name[1]))
            return name.Substring(1);

        return name;
    }
}
