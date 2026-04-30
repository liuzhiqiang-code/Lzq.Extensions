using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using LzqNet.Extensions.Azure.Configuration;
using LzqNet.Extensions.Azure.Exceptions;
using LzqNet.Extensions.Azure.Interfaces;
using LzqNet.Extensions.Azure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Diagnostics;

namespace LzqNet.Extensions.Azure.Services;

/// <summary>
/// Table 存储服务实现
/// </summary>
public class TableStorageService : ITableStorageService
{
    private readonly TableServiceClient _serviceClient;
    private readonly ILogger<TableStorageService> _logger;
    private readonly TableStorageOptions _options;
    private readonly AsyncPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy? _circuitBreakerPolicy;

    public TableStorageService(
        IOptions<TableStorageOptions> options,
        ILogger<TableStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _serviceClient = InitializeClient();
        _retryPolicy = BuildRetryPolicy();
        if (_options.RetryOptions.EnableCircuitBreaker)
        {
            _circuitBreakerPolicy = BuildCircuitBreakerPolicy();
        }
    }

    #region 核心接口实现

    public async Task<TableOperationResult<T>> InsertAsync<T>(T entity, CancellationToken ct = default) where T : TableEntityBase
    {
        return await ExecuteBaseAsync(async () =>
        {
            var client = GetClient<T>();
            if (_options.CreateTableIfNotExists) await client.CreateIfNotExistsAsync(ct);
            await client.AddEntityAsync(entity, ct);
            return entity;
        }, "Insert", ct);
    }

    public async Task<TableOperationResult<T>> UpsertAsync<T>(T entity, CancellationToken ct = default) where T : TableEntityBase
    {
        return await ExecuteBaseAsync(async () =>
        {
            await GetClient<T>().UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            return entity;
        }, "Upsert", ct);
    }

    public async Task<TableOperationResult<T>> UpdateAsync<T>(T entity, CancellationToken ct = default) where T : TableEntityBase
    {
        return await ExecuteBaseAsync(async () =>
        {
            await GetClient<T>().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
            return entity;
        }, "Update", ct);
    }

    public async Task<TableOperationResult<T>> GetAsync<T>(string pk, string rk, CancellationToken ct = default) where T : TableEntityBase, new()
    {
        try
        {
            var response = await GetClient<T>().GetEntityAsync<T>(pk, rk, cancellationToken: ct);
            return TableOperationResult<T>.Success(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return TableOperationResult<T>.Failure("实体未找到", "EntityNotFound");
        }
        catch (Exception ex)
        {
            return TableOperationResult<T>.Failure(ex.Message);
        }
    }

    public async Task<TableOperationResult> DeleteAsync<T>(string pk, string rk, CancellationToken ct = default) where T : TableEntityBase
    {
        try
        {
            await ExecuteWithPolicyAsync(() => GetClient<T>().DeleteEntityAsync(pk, rk, ETag.All, ct), "Delete", ct);
            return TableOperationResult.Success();
        }
        catch (Exception ex) { return TableOperationResult.Failure(ex.Message); }
    }

    public async Task<TableOperationResult<int>> BatchUpsertAsync<T>(IEnumerable<T> entities, CancellationToken ct = default) where T : TableEntityBase
    {
        if (entities == null || !entities.Any()) return TableOperationResult<int>.Success(0);
        var totalProcessed = 0;
        try
        {
            var client = GetClient<T>();
            var groups = entities.GroupBy(e => e.PartitionKey);
            foreach (var group in groups)
            {
                foreach (var chunk in group.Chunk(100))
                {
                    var actions = chunk.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e));
                    await ExecuteWithPolicyAsync(() => client.SubmitTransactionAsync(actions, ct), $"BatchUpsert_{group.Key}", ct);
                    totalProcessed += chunk.Length;
                }
            }
            return TableOperationResult<int>.Success(totalProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量 Upsert 中断，已完成: {Count}", totalProcessed);
            return TableOperationResult<int>.Failure(ex.Message);
        }
    }

    public async Task<TableOperationResult<int>> BatchDeleteAsync<T>(IEnumerable<T> entities, CancellationToken ct = default) where T : TableEntityBase
    {
        if (entities == null || !entities.Any()) return TableOperationResult<int>.Success(0);
        var totalDeleted = 0;
        try
        {
            var client = GetClient<T>();
            var groups = entities.GroupBy(e => e.PartitionKey);
            foreach (var group in groups)
            {
                foreach (var chunk in group.Chunk(100))
                {
                    var actions = chunk.Select(e => new TableTransactionAction(TableTransactionActionType.Delete, e, ETag.All));
                    await ExecuteWithPolicyAsync(() => client.SubmitTransactionAsync(actions, ct), $"BatchDelete_{group.Key}", ct);
                    totalDeleted += chunk.Length;
                }
            }
            return TableOperationResult<int>.Success(totalDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量删除中断，已完成: {Count}", totalDeleted);
            return TableOperationResult<int>.Failure(ex.Message);
        }
    }

    public async IAsyncEnumerable<T> QueryAsync<T>(TableQueryOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) where T : TableEntityBase, new()
    {
        var client = GetClient<T>();
        var filter = BuildFilter(options);
        var query = client.QueryAsync<T>(
            filter: string.IsNullOrEmpty(filter) ? null : filter,
            maxPerPage: options?.MaxPerPage,
            select: options?.SelectColumns,
            cancellationToken: ct);

        await foreach (var item in query.WithCancellation(ct))
        {
            yield return item;
        }
    }

    public async Task<TableOperationResult<List<T>>> QueryAllAsync<T>(TableQueryOptions? options = null, CancellationToken ct = default) where T : TableEntityBase, new()
    {
        var list = new List<T>();
        await foreach (var item in QueryAsync<T>(options, ct)) list.Add(item);
        return TableOperationResult<List<T>>.Success(list);
    }

    /// <summary>
    /// 检查表是否存在
    /// </summary>
    public async Task<TableOperationResult<bool>> TableExistsAsync<T>(CancellationToken ct = default) where T : TableEntityBase
    {
        try
        {
            var tableName = typeof(T).GetTableName();
            // 在 12.11.0 中，最有效的办法是尝试获取该表的 ServiceClient 过滤
            // 或者直接调用 QueryAsync 查表名（如下）
            var query = _serviceClient.QueryAsync(filter: $"TableName eq '{tableName}'", cancellationToken: ct);
            await foreach (var table in query.WithCancellation(ct))
            {
                return TableOperationResult<bool>.Success(true);
            }
            return TableOperationResult<bool>.Success(false);
        }
        catch (Exception ex)
        {
            return TableOperationResult<bool>.Failure(ex.Message);
        }
    }

    #endregion

    #region 私有辅助逻辑

    private TableServiceClient InitializeClient()
    {
        var clientOptions = new TableClientOptions();
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(_options.RetryOptions.NetworkTimeoutSeconds);

        if (_options.UseManagedIdentity)
        {
            return new TableServiceClient(new Uri(_options.StorageAccountUri!), new DefaultAzureCredential(), clientOptions);
        }
        return new TableServiceClient(_options.ConnectionString!, clientOptions);
    }

    private TableClient GetClient<T>() where T : TableEntityBase => _serviceClient.GetTableClient(typeof(T).GetTableName());

    private async Task<TableOperationResult<T>> ExecuteBaseAsync<T>(Func<Task<T>> operation, string opName, CancellationToken ct)
    {
        try
        {
            var data = await ExecuteWithPolicyAsync(operation, opName, ct);
            return TableOperationResult<T>.Success(data);
        }
        catch (TableStorageException ex)
        {
            return TableOperationResult<T>.Failure(ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            return TableOperationResult<T>.Failure(ex.Message);
        }
    }

    private async Task<TResult> ExecuteWithPolicyAsync<TResult>(Func<Task<TResult>> operation, string operationName, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;
        try
        {
            var policy = _circuitBreakerPolicy != null ? _circuitBreakerPolicy.WrapAsync(_retryPolicy) : _retryPolicy;
            return await policy.ExecuteAsync(async (context, token) =>
            {
                retryCount = context.TryGetValue("RetryCount", out var count) ? (int)count : 0;
                context["RetryCount"] = retryCount + 1;
                return await operation();
            }, new Context(operationName), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Op} 失败，重试 {Retry} 次，耗时 {Ms}ms", operationName, retryCount, stopwatch.ElapsedMilliseconds);
            throw new TableStorageException($"{operationName} 失败", (ex as RequestFailedException)?.ErrorCode, retryCount, ex);
        }
    }

    private AsyncRetryPolicy BuildRetryPolicy()
    {
        return Policy.Handle<RequestFailedException>(ex => ex.Status >= 500 || ex.Status == 429)
            .Or<TimeoutException>()
            .WaitAndRetryAsync(_options.RetryOptions.MaxRetries, retryCount =>
            {
                var delay = _options.RetryOptions.Mode == RetryMode.Exponential
                    ? TimeSpan.FromMilliseconds(Math.Min(_options.RetryOptions.DelayMilliseconds * Math.Pow(2, retryCount - 1), _options.RetryOptions.MaxDelayMilliseconds))
                    : TimeSpan.FromMilliseconds(_options.RetryOptions.DelayMilliseconds);
                return delay + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * delay.TotalMilliseconds * 0.2);
            }, (ex, time, count, ctx) =>
            {
                if (_options.RetryOptions.EnableRetryLogging)
                    _logger.LogWarning("Table 重试 {Count}/{Max}", count, _options.RetryOptions.MaxRetries);
            });
    }

    private AsyncCircuitBreakerPolicy BuildCircuitBreakerPolicy()
    {
        return Policy.Handle<Exception>().CircuitBreakerAsync(
            _options.RetryOptions.CircuitBreakerFailureThreshold,
            TimeSpan.FromSeconds(_options.RetryOptions.CircuitBreakerRecoverySeconds));
    }

    private string BuildFilter(TableQueryOptions? options)
    {
        if (options == null) return string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(options.PartitionKey))
            parts.Add(TableClient.CreateQueryFilter($"PartitionKey eq {options.PartitionKey}"));
        if (!string.IsNullOrEmpty(options.RowKey))
            parts.Add(TableClient.CreateQueryFilter($"RowKey eq {options.RowKey}"));
        if (!string.IsNullOrEmpty(options.Filter))
            parts.Add($"({options.Filter})");
        return string.Join(" and ", parts);
    }

    #endregion
}