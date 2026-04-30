namespace LzqNet.Extensions.Azure.Configuration;

public class BlobStorageOptions
{
    /// <summary>
    /// 连接字符串（优先使用托管身份时可为空）
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 容器名称
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// 是否自动创建容器
    /// </summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>
    /// 是否使用托管身份（推荐生产环境）
    /// </summary>
    public bool UseManagedIdentity { get; set; } = false;

    /// <summary>
    /// 存储账户URI（使用托管身份时必需）
    /// </summary>
    public string? StorageAccountUri { get; set; }

    /// <summary>
    /// 默认上传超时时间（秒）
    /// </summary>
    public int DefaultUploadTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 默认下载超时时间（秒）
    /// </summary>
    public int DefaultDownloadTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 是否启用服务器端加密
    /// </summary>
    public bool EnableServerSideEncryption { get; set; } = true;

    /// <summary>
    /// 重试策略配置
    /// </summary>
    public StorageRetryOptions RetryOptions { get; set; }
}