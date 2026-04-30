using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using LzqNet.Extensions.Azure.Configuration;
using LzqNet.Extensions.Azure.Exceptions;
using LzqNet.Extensions.Azure.Interfaces;
using LzqNet.Extensions.Azure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LzqNet.Extensions.Azure.Services;


/// <summary>
/// Blob存储服务实现（生产级）
/// </summary>
public class BlobStorageService : IBlobStorageService, IAsyncDisposable
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<BlobStorageService> _logger;
    private readonly BlobStorageOptions _options;
    private readonly AsyncPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy? _circuitBreakerPolicy;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private bool _disposed;

    public BlobStorageService(
        IOptions<BlobStorageOptions> options,
        ILogger<BlobStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 验证配置
        ValidateOptions();

        // 初始化Blob客户端
        _containerClient = InitializeBlobClient();

        // 构建重试策略
        _retryPolicy = BuildRetryPolicy();

        // 构建熔断器策略（可选）
        if (_options.RetryOptions.EnableCircuitBreaker)
        {
            _circuitBreakerPolicy = BuildCircuitBreakerPolicy();
        }

        // 确保容器存在
        EnsureContainerExists().GetAwaiter().GetResult();
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrEmpty(_options.ContainerName))
        {
            throw new ArgumentException("容器名称不能为空", nameof(_options.ContainerName));
        }

        if (!_options.UseManagedIdentity && string.IsNullOrEmpty(_options.ConnectionString))
        {
            throw new ArgumentException("必须提供连接字符串或启用托管身份");
        }

        if (_options.UseManagedIdentity && string.IsNullOrEmpty(_options.StorageAccountUri))
        {
            throw new ArgumentException("使用托管身份时必须提供存储账户URI");
        }
    }

    private BlobContainerClient InitializeBlobClient()
    {
        BlobServiceClient blobServiceClient;

        if (_options.UseManagedIdentity)
        {
            // 使用托管身份（生产环境推荐）
            var credential = new DefaultAzureCredential();
            blobServiceClient = new BlobServiceClient(
                new Uri(_options.StorageAccountUri!),
                credential,
                GetBlobClientOptions());
        }
        else
        {
            // 使用连接字符串
            blobServiceClient = new BlobServiceClient(
                _options.ConnectionString!,
                GetBlobClientOptions());
        }

        return blobServiceClient.GetBlobContainerClient(_options.ContainerName);
    }

    private BlobClientOptions GetBlobClientOptions()
    {
        var options = new BlobClientOptions
        {
            Retry = {
                Mode = _options.RetryOptions.Mode,
                MaxRetries = _options.RetryOptions.MaxRetries,
                Delay = TimeSpan.FromMilliseconds(_options.RetryOptions.DelayMilliseconds),
                MaxDelay = TimeSpan.FromMilliseconds(_options.RetryOptions.MaxDelayMilliseconds),
                NetworkTimeout = TimeSpan.FromSeconds(_options.RetryOptions.NetworkTimeoutSeconds)
            }
        };

        return options;
    }

    private AsyncRetryPolicy BuildRetryPolicy()
    {
        return Policy
            .Handle<RequestFailedException>(ex =>
                ex.Status >= 500 ||      // 服务端错误
                ex.Status == 408 ||      // 请求超时
                ex.Status == 429)        // 请求限流
            .Or<IOException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: _options.RetryOptions.MaxRetries,
                sleepDurationProvider: (retryCount) =>
                {
                    // 根据配置的重试模式计算延迟
                    TimeSpan delay;

                    switch (_options.RetryOptions.Mode)
                    {
                        case RetryMode.Fixed:
                            // 固定延迟
                            delay = TimeSpan.FromMilliseconds(_options.RetryOptions.DelayMilliseconds);
                            break;

                        case RetryMode.Exponential:
                        default:
                            // 指数退避: delay = min(baseDelay * 2^(retryCount-1), maxDelay)
                            // retryCount 从 1 开始（第一次重试）
                            var exponentialDelay = Math.Min(
                                _options.RetryOptions.DelayMilliseconds * Math.Pow(2, retryCount - 1),
                                _options.RetryOptions.MaxDelayMilliseconds);
                            delay = TimeSpan.FromMilliseconds(exponentialDelay);
                            break;
                    }

                    // 添加随机抖动（±10%），避免惊群效应
                    var jitter = TimeSpan.FromMilliseconds(
                        Random.Shared.NextDouble() * delay.TotalMilliseconds * 0.2 - delay.TotalMilliseconds * 0.1);

                    return delay + jitter;
                },
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    if (_options.RetryOptions.EnableRetryLogging)
                    {
                        var requestFailedEx = exception as RequestFailedException;
                        var statusCode = requestFailedEx?.Status ?? 0;

                        _logger.LogWarning(exception,
                            "Blob操作第 {RetryCount}/{MaxRetries} 次重试，等待 {DelayMs:F0}ms，状态码: {StatusCode}",
                            retryCount,
                            _options.RetryOptions.MaxRetries,
                            timeSpan.TotalMilliseconds,
                            statusCode);
                    }
                });
    }

    private AsyncCircuitBreakerPolicy BuildCircuitBreakerPolicy()
    {
        return Policy
            .Handle<RequestFailedException>(ex => ex.Status >= 500)
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: _options.RetryOptions.CircuitBreakerFailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(_options.RetryOptions.CircuitBreakerRecoverySeconds),
                onBreak: (exception, breakDuration, context) =>
                {
                    _logger.LogError(exception,
                        "熔断器打开，持续 {BreakDuration}s，失败次数已达阈值",
                        breakDuration.TotalSeconds);
                },
                onReset: (context) =>
                {
                    _logger.LogInformation("熔断器关闭，服务恢复");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("熔断器半开，尝试恢复");
                });
    }

    private async Task EnsureContainerExists()
    {
        try
        {
            if (_options.CreateContainerIfNotExists)
            {
                await _containerClient.CreateIfNotExistsAsync();
                _logger.LogInformation("容器 {ContainerName} 已就绪", _options.ContainerName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化容器 {ContainerName} 失败", _options.ContainerName);
            throw new BlobStorageException($"容器初始化失败: {_options.ContainerName}", ex);
        }
    }

    private async Task<T> ExecuteWithPolicyAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        string? blobName = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;

        try
        {
            var policy = _circuitBreakerPolicy != null
                ? _circuitBreakerPolicy.WrapAsync(_retryPolicy)
                : _retryPolicy;

            var result = await policy.ExecuteAsync(async (context, ct) =>
            {
                retryCount = context.TryGetValue("RetryCount", out var count) ? (int)count : 0;
                context["RetryCount"] = retryCount + 1;

                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            }, new Context(operationName), cancellationToken);

            stopwatch.Stop();

            if (retryCount > 0)
            {
                _logger.LogInformation("{OperationName} 在 {RetryCount} 次重试后成功，耗时 {Duration}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "{OperationName} 失败，耗时 {Duration}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            throw new BlobStorageException(
                $"{operationName} 失败: {ex.Message}",
                (ex as RequestFailedException)?.ErrorCode,
                retryCount,
                ex);
        }
    }

    public async Task<BlobOperationResult<string>> UploadAsync(
        string filePath,
        string? blobName = null,
        CancellationToken cancellationToken = default)
    {
        var operationName = "UploadFile";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!File.Exists(filePath))
            {
                return BlobOperationResult<string>.Failure($"文件不存在: {filePath}", "FileNotFound");
            }

            blobName ??= GenerateBlobName(Path.GetFileName(filePath));

            var blobClient = _containerClient.GetBlobClient(blobName);

            // 使用信号量控制并发文件访问
            var fileLock = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync(cancellationToken);

            try
            {
                await ExecuteWithPolicyAsync(async () =>
                {
                    var uploadOptions = new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders
                        {
                            ContentType = GetContentType(filePath)
                        },
                        Conditions = new BlobRequestConditions(),
                        TransferOptions = new StorageTransferOptions
                        {
                            MaximumConcurrency = 4,
                            MaximumTransferSize = 4 * 1024 * 1024 // 4MB
                        }
                    };

                    await blobClient.UploadAsync(filePath, uploadOptions, cancellationToken);
                    return true;
                }, operationName, blobName, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation("文件上传成功: {BlobName}，耗时 {Duration}ms", blobName, stopwatch.ElapsedMilliseconds);

                return BlobOperationResult<string>.Success(blobClient.Uri.ToString());
            }
            finally
            {
                fileLock.Release();
            }
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult<string>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传文件异常: {FilePath}", filePath);
            return BlobOperationResult<string>.Failure($"上传失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<string>> UploadFromBytesAsync(
        byte[] content,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content);
        return await UploadFromStreamAsync(stream, blobName, cancellationToken);
    }

    public async Task<BlobOperationResult<string>> UploadFromStreamAsync(
        Stream stream,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var operationName = "UploadStream";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrEmpty(blobName))
            {
                return BlobOperationResult<string>.Failure("Blob名称不能为空", "InvalidBlobName");
            }

            if (!stream.CanRead)
            {
                return BlobOperationResult<string>.Failure("流不可读", "InvalidStream");
            }

            var blobClient = _containerClient.GetBlobClient(blobName);

            await ExecuteWithPolicyAsync(async () =>
            {
                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = GetContentType(blobName) // 利用你已有的私有方法
                    },
                    TransferOptions = new StorageTransferOptions
                    {
                        MaximumConcurrency = 4,
                        MaximumTransferSize = 4 * 1024 * 1024
                    }
                };

                await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
                return true;
            }, operationName, blobName, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("流上传成功: {BlobName}，耗时 {Duration}ms", blobName, stopwatch.ElapsedMilliseconds);

            return BlobOperationResult<string>.Success(blobClient.Uri.ToString());
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult<string>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult<string>.Failure($"流上传失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult> DownloadAsync(
        string blobName,
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        var operationName = "DownloadFile";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            // 创建目录（如果不存在）
            var directory = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await ExecuteWithPolicyAsync(async () =>
            {
                var downloadOptions = new BlobDownloadToOptions
                {
                    TransferOptions = new StorageTransferOptions
                    {
                        MaximumConcurrency = 4,
                        MaximumTransferSize = 4 * 1024 * 1024
                    }
                };

                await blobClient.DownloadToAsync(localFilePath, downloadOptions, cancellationToken);
                return true;
            }, operationName, blobName, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("文件下载成功: {BlobName} -> {LocalPath}，耗时 {Duration}ms",
                blobName, localFilePath, stopwatch.ElapsedMilliseconds);

            return BlobOperationResult.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BlobOperationResult.Failure($"Blob不存在: {blobName}", "BlobNotFound");
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult.Failure($"下载失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<Stream>> DownloadToStreamAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var operationName = "DownloadToStream";

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            var memoryStream = new MemoryStream();

            await ExecuteWithPolicyAsync(async () =>
            {
                var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                await response.Value.Content.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;
                return true;
            }, operationName, blobName, cancellationToken);

            return BlobOperationResult<Stream>.Success(memoryStream);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BlobOperationResult<Stream>.Failure($"Blob不存在: {blobName}", "BlobNotFound");
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult<Stream>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult<Stream>.Failure($"下载失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<bool>> DeleteAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var operationName = "DeleteBlob";

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            var result = await ExecuteWithPolicyAsync(async () =>
            {
                var response = await blobClient.DeleteIfExistsAsync(
                    DeleteSnapshotsOption.IncludeSnapshots,
                    cancellationToken: cancellationToken);
                return response.Value;
            }, operationName, blobName, cancellationToken);

            if (result)
            {
                _logger.LogInformation("Blob删除成功: {BlobName}", blobName);
            }

            return BlobOperationResult<bool>.Success(result);
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult<bool>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult<bool>.Failure($"删除失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<bool>> ExistsAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var operationName = "CheckExists";

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            var result = await ExecuteWithPolicyAsync(async () =>
            {
                return await blobClient.ExistsAsync(cancellationToken);
            }, operationName, blobName, cancellationToken);

            return BlobOperationResult<bool>.Success(result);
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult<bool>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult<bool>.Failure($"检查失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<BlobMetadata>> GetMetadataAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var operationName = "GetMetadata";

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            var properties = await ExecuteWithPolicyAsync(async () =>
            {
                return await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            }, operationName, blobName, cancellationToken);

            var metadata = new BlobMetadata
            {
                Name = blobName,
                Size = properties.Value.ContentLength,
                LastModified = properties.Value.LastModified.DateTime,
                ContentType = properties.Value.ContentType ?? "application/octet-stream",
                ContentHash = properties.Value.ContentHash != null
                    ? Convert.ToBase64String(properties.Value.ContentHash)
                    : null,
                Metadata = properties.Value.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new(),
                ETag = properties.Value.ETag.ToString()
            };

            return BlobOperationResult<BlobMetadata>.Success(metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BlobOperationResult<BlobMetadata>.Failure($"Blob不存在: {blobName}", "BlobNotFound");
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult<BlobMetadata>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult<BlobMetadata>.Failure($"获取元数据失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult> SetMetadataAsync(
        string blobName,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        var operationName = "SetMetadata";

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            await ExecuteWithPolicyAsync(async () =>
            {
                await blobClient.SetMetadataAsync(metadata, cancellationToken: cancellationToken);
                return true;
            }, operationName, blobName, cancellationToken);

            _logger.LogInformation("元数据设置成功: {BlobName}, 键数: {Count}", blobName, metadata.Count);
            return BlobOperationResult.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BlobOperationResult.Failure($"Blob不存在: {blobName}", "BlobNotFound");
        }
        catch (BlobStorageException ex)
        {
            return BlobOperationResult.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return BlobOperationResult.Failure($"设置元数据失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<string>> GetBlobUriWithSasAsync(
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        var operationName = "GetSasUri";

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobName);

            var exists = await ExistsAsync(blobName, cancellationToken);
            if (!exists.IsSuccess || !exists.Data)
            {
                return BlobOperationResult<string>.Failure($"Blob不存在: {blobName}", "BlobNotFound");
            }

            // 创建SAS令牌
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = blobName,
                Resource = "b", // b = blob, c = container
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry
                )
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return BlobOperationResult<string>.Success(sasUri.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成SAS URI失败: {BlobName}", blobName);
            return BlobOperationResult<string>.Failure($"生成SAS URI失败: {ex.Message}");
        }
    }

    public async Task<BlobOperationResult<int>> BatchDeleteAsync(
        IEnumerable<string> blobNames,
        CancellationToken cancellationToken = default)
    {
        var operationName = "BatchDelete";
        var deletedCount = 0;
        var failures = new List<string>();

        foreach (var blobName in blobNames)
        {
            try
            {
                var result = await DeleteAsync(blobName, cancellationToken);
                if (result.IsSuccess && result.Data)
                {
                    deletedCount++;
                }
                else if (!result.IsSuccess)
                {
                    failures.Add($"{blobName}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{blobName}: {ex.Message}");
                _logger.LogError(ex, "批量删除失败: {BlobName}", blobName);
            }
        }

        if (failures.Count > 0)
        {
            _logger.LogWarning("批量删除部分失败: {FailuresCount}/{Total}", failures.Count, blobNames.Count());
            return BlobOperationResult<int>.Failure($"部分删除失败: {string.Join("; ", failures)}");
        }

        _logger.LogInformation("批量删除成功: {DeletedCount} 个Blob", deletedCount);
        return BlobOperationResult<int>.Success(deletedCount);
    }

    public async IAsyncEnumerable<string> ListBlobsAsync(
        string? prefix = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 获取异步分页集合
        var asyncPageable = _containerClient.GetBlobsAsync(
            traits: BlobTraits.None,      // 不获取额外元数据，提高性能
            states: BlobStates.None,       // 只获取活跃的 Blob
            prefix: prefix,
            cancellationToken: cancellationToken);

        // 遍历所有分页
        await foreach (var blobItem in asyncPageable.WithCancellation(cancellationToken))
        {
            yield return blobItem.Name;
        }
    }

    private string GenerateBlobName(string originalFileName)
    {
        var date = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);

        // 防止文件名过长
        if (fileName.Length > 50)
        {
            fileName = fileName.Substring(0, 50);
        }

        return $"{date}/{fileName}_{uniqueId}{extension}";
    }

    private static string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            foreach (var fileLock in _fileLocks.Values)
            {
                fileLock.Dispose();
            }
            _fileLocks.Clear();

            _disposed = true;
        }

        await Task.CompletedTask;
    }
}