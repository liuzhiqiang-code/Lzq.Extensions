using LzqNet.Extensions.Azure.Configuration;
using LzqNet.Extensions.Azure.Interfaces;
using LzqNet.Extensions.Azure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LzqNet.Extensions.Azure.Extensions;

public static class BlobStorageExtensions
{
    /// <summary>
    /// 添加Lzq Azure Blob存储服务（从配置文件加载）
    /// </summary>
    /// <param name="builder">宿主应用程序构建器</param>
    /// <param name="configSectionName">配置节名称，默认 "AzureStorage"</param>
    /// <returns>返回 IHostApplicationBuilder 支持链式调用</returns>
    public static IHostApplicationBuilder AddLzqAzureBlobStorage(
        this IHostApplicationBuilder builder,
        string configSectionName = "AzureStorage")
    {
        // 使用 AddOptions + BindConfiguration + Validate 方式
        builder.Services.AddOptions<BlobStorageOptions>()
            .BindConfiguration(configSectionName)
            .Validate(options => ValidateOptions(options, out var errors),
                $"Blob存储配置验证失败，请检查配置节 '{configSectionName}'")
            .ValidateOnStart();

        // 注册服务
        builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

        return builder;
    }

    /// <summary>
    /// 添加Lzq Azure Blob存储服务（使用委托配置）
    /// </summary>
    /// <param name="builder">宿主应用程序构建器</param>
    /// <param name="configureOptions">配置委托</param>
    /// <returns>返回 IHostApplicationBuilder 支持链式调用</returns>
    public static IHostApplicationBuilder AddLzqAzureBlobStorage(
        this IHostApplicationBuilder builder,
        Action<BlobStorageOptions> configureOptions)
    {
        // 使用 AddOptions + Configure + Validate 方式
        builder.Services.AddOptions<BlobStorageOptions>()
            .Configure(configureOptions)
            .Validate(options => ValidateOptions(options, out var errors),
                $"Blob存储配置验证失败")
            .ValidateOnStart();

        // 注册服务
        builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

        return builder;
    }

    /// <summary>
    /// 验证配置选项
    /// </summary>
    private static bool ValidateOptions(BlobStorageOptions options, out List<string> errors)
    {
        errors = new List<string>();

        // 验证容器名称
        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            errors.Add("容器名称不能为空");
        }
        else if (!IsValidContainerName(options.ContainerName))
        {
            errors.Add($"容器名称 '{options.ContainerName}' 格式无效。容器名称只能包含小写字母、数字和连字符，必须以字母或数字开头和结尾");
        }

        // 验证连接字符串或托管身份配置
        if (!options.UseManagedIdentity)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                errors.Add("未启用托管身份时，必须提供连接字符串");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.StorageAccountUri))
            {
                errors.Add("启用托管身份时，必须提供存储账户URI");
            }
            else if (!Uri.IsWellFormedUriString(options.StorageAccountUri, UriKind.Absolute))
            {
                errors.Add($"存储账户URI '{options.StorageAccountUri}' 格式无效");
            }
        }

        // 验证重试配置
        if (options.RetryOptions.MaxRetries < 0 || options.RetryOptions.MaxRetries > 10)
        {
            errors.Add("最大重试次数必须在 0-10 之间");
        }

        if (options.RetryOptions.DelayMilliseconds < 0)
        {
            errors.Add("延迟时间不能为负数");
        }

        if (options.RetryOptions.MaxDelayMilliseconds < options.RetryOptions.DelayMilliseconds)
        {
            errors.Add("最大延迟时间不能小于基础延迟时间");
        }

        if (options.RetryOptions.NetworkTimeoutSeconds <= 0)
        {
            errors.Add("网络超时时间必须大于0");
        }

        // 熔断器配置验证
        if (options.RetryOptions.EnableCircuitBreaker)
        {
            if (options.RetryOptions.CircuitBreakerFailureThreshold <= 0)
            {
                errors.Add("熔断器失败阈值必须大于0");
            }

            if (options.RetryOptions.CircuitBreakerRecoverySeconds <= 0)
            {
                errors.Add("熔断器恢复时间必须大于0");
            }
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// 验证容器名称格式
    /// </summary>
    private static bool IsValidContainerName(string containerName)
    {
        // Azure 容器名称规则：
        // - 长度 3-63 个字符
        // - 只能包含小写字母、数字和连字符
        // - 必须以字母或数字开头和结尾
        if (containerName.Length < 3 || containerName.Length > 63)
            return false;

        if (!char.IsLetterOrDigit(containerName[0]) || !char.IsLetterOrDigit(containerName[^1]))
            return false;

        foreach (var ch in containerName)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-')
                return false;
        }

        return true;
    }
}
