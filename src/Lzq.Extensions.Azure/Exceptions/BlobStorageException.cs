namespace LzqNet.Extensions.Azure.Exceptions;

/// <summary>
/// Blob存储异常
/// </summary>
public class BlobStorageException : Exception
{
    public string? ErrorCode { get; set; }
    public string? BlobName { get; set; }
    public int RetryCount { get; set; }

    public BlobStorageException() : base() { }

    public BlobStorageException(string message) : base(message) { }

    public BlobStorageException(string message, Exception innerException) : base(message, innerException) { }

    public BlobStorageException(string message, string? errorCode, string? blobName = null)
        : base(message)
    {
        ErrorCode = errorCode;
        BlobName = blobName;
    }

    public BlobStorageException(string message, string? errorCode, int retryCount, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        RetryCount = retryCount;
    }
}