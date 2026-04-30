using LzqNet.Extensions.Azure.Configuration;
using LzqNet.Extensions.Azure.Interfaces;
using LzqNet.Extensions.Azure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LzqNet.Extensions.Azure.Extensions;

public static class TableStorageExtensions
{
    /// <summary>
    /// 添加 Table 存储服务（从配置文件加载）
    /// </summary>
    public static IHostApplicationBuilder AddLzqAzureTableStorage(
        this IHostApplicationBuilder builder,
        string configSectionName = "AzureTableStorage")
    {
        builder.Services.AddOptions<TableStorageOptions>()
            .BindConfiguration(configSectionName)
            .Validate(options => ValidateOptions(options),
                $"Table 存储配置验证失败，请检查配置节 '{configSectionName}'")
            .ValidateOnStart();

        builder.Services.AddScoped<ITableStorageService, TableStorageService>();

        return builder;
    }

    /// <summary>
    /// 添加 Table 存储服务（使用委托配置）
    /// </summary>
    public static IHostApplicationBuilder AddLzqAzureTableStorage(
        this IHostApplicationBuilder builder,
        Action<TableStorageOptions> configureOptions)
    {
        builder.Services.AddOptions<TableStorageOptions>()
            .Configure(configureOptions)
            .Validate(options => ValidateOptions(options),
                "Table 存储配置验证失败")
            .ValidateOnStart();

        builder.Services.AddScoped<ITableStorageService, TableStorageService>();

        return builder;
    }

    private static bool ValidateOptions(TableStorageOptions options)
    {
        if (!options.UseManagedIdentity && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return false;
        }

        if (options.UseManagedIdentity && string.IsNullOrWhiteSpace(options.StorageAccountUri))
        {
            return false;
        }

        return true;
    }
}
