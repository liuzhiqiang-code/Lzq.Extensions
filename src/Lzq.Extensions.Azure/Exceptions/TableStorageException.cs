namespace LzqNet.Extensions.Azure.Exceptions;

/// <summary>
/// Table存储专用异常
/// </summary>
public class TableStorageException : Exception
{
    public string? ErrorCode { get; }
    public int RetryCount { get; }

    public TableStorageException(string message, string? errorCode = null, int retryCount = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        RetryCount = retryCount;
    }
}