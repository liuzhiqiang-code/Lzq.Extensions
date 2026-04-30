namespace LzqNet.Extensions.Azure.Configuration;

/// <summary>
/// Table 存储配置选项
/// </summary>
public class TableStorageOptions
{
    /// <summary>
    /// 连接字符串（优先使用托管身份时可为空）
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 是否自动创建表
    /// </summary>
    public bool CreateTableIfNotExists { get; set; } = true;

    /// <summary>
    /// 是否使用托管身份（推荐生产环境）
    /// </summary>
    public bool UseManagedIdentity { get; set; } = false;

    /// <summary>
    /// 存储账户URI（使用托管身份时必需）
    /// </summary>
    public string? StorageAccountUri { get; set; }

    /// <summary>
    /// 默认操作超时时间（秒）
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 重试策略配置
    /// </summary>
    public StorageRetryOptions RetryOptions { get; set; } = new();
}
