using Azure.Core;

namespace LzqNet.Extensions.Azure.Configuration;

public class StorageRetryOptions
{
    /// <summary>
    /// 重试模式：Fixed, Exponential
    /// </summary>
    public RetryMode Mode { get; set; } = RetryMode.Exponential;

    /// <summary>
    /// 最大重试次数（0-10）
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 基础延迟时间（毫秒）
    /// </summary>
    public int DelayMilliseconds { get; set; } = 100;

    /// <summary>
    /// 最大延迟时间（毫秒）
    /// </summary>
    public int MaxDelayMilliseconds { get; set; } = 30000;

    /// <summary>
    /// 网络超时时间（秒）
    /// </summary>
    public int NetworkTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// 是否启用熔断器
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = false;

    /// <summary>
    /// 熔断器失败阈值（连续失败次数）
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// 熔断器恢复时间（秒）
    /// </summary>
    public int CircuitBreakerRecoverySeconds { get; set; } = 30;

    /// <summary>
    /// 是否启用请求重试日志
    /// </summary>
    public bool EnableRetryLogging { get; set; } = true;
}
