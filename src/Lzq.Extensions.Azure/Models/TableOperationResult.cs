namespace LzqNet.Extensions.Azure.Models;

public class TableOperationResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime OperationTime { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; }
    public TimeSpan Duration { get; set; }

    public static TableOperationResult Success() => new() { IsSuccess = true };
    public static TableOperationResult Failure(string errorMessage, string? errorCode = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

public class TableOperationResult<T> : TableOperationResult
{
    public T? Data { get; set; }

    public static TableOperationResult<T> Success(T data) =>
        new() { IsSuccess = true, Data = data };

    public new static TableOperationResult<T> Failure(string errorMessage, string? errorCode = null) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}