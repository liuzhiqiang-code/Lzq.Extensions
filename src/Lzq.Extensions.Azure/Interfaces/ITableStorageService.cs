using LzqNet.Extensions.Azure.Models;

namespace LzqNet.Extensions.Azure.Interfaces;

public interface ITableStorageService
{
    // 基础操作
    Task<TableOperationResult<T>> InsertAsync<T>(T entity, CancellationToken ct = default) where T : TableEntityBase;
    Task<TableOperationResult<T>> UpsertAsync<T>(T entity, CancellationToken ct = default) where T : TableEntityBase;
    Task<TableOperationResult<T>> UpdateAsync<T>(T entity, CancellationToken ct = default) where T : TableEntityBase;
    Task<TableOperationResult<T>> GetAsync<T>(string pk, string rk, CancellationToken ct = default) where T : TableEntityBase, new();
    Task<TableOperationResult> DeleteAsync<T>(string pk, string rk, CancellationToken ct = default) where T : TableEntityBase;

    // 批量操作 (需确保同一 PartitionKey)
    Task<TableOperationResult<int>> BatchUpsertAsync<T>(IEnumerable<T> entities, CancellationToken ct = default) where T : TableEntityBase;
    Task<TableOperationResult<int>> BatchDeleteAsync<T>(IEnumerable<T> entities, CancellationToken ct = default) where T : TableEntityBase;

    // 查询操作
    IAsyncEnumerable<T> QueryAsync<T>(TableQueryOptions? options = null, CancellationToken ct = default) where T : TableEntityBase, new();
    Task<TableOperationResult<List<T>>> QueryAllAsync<T>(TableQueryOptions? options = null, CancellationToken ct = default) where T : TableEntityBase, new();

    // 管理操作
    Task<TableOperationResult<bool>> TableExistsAsync<T>(CancellationToken ct = default) where T : TableEntityBase;
}