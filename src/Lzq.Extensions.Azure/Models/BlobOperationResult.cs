namespace LzqNet.Extensions.Azure.Models;

/// <summary>
/// Blob操作结果（无返回值）
/// </summary>
public class BlobOperationResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime OperationTime { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; }
    public TimeSpan Duration { get; set; }

    public static BlobOperationResult Success() => new() { IsSuccess = true };
    public static BlobOperationResult Failure(string errorMessage, string? errorCode = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

/// <summary>
/// Blob操作结果（带返回值）
/// </summary>
public class BlobOperationResult<T> : BlobOperationResult
{
    public T? Data { get; set; }

    public static BlobOperationResult<T> Success(T data) =>
        new() { IsSuccess = true, Data = data };

    public new static BlobOperationResult<T> Failure(string errorMessage, string? errorCode = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

/// <summary>
/// Blob元数据
/// </summary>
public class BlobMetadata
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public string ETag { get; set; } = string.Empty;
}