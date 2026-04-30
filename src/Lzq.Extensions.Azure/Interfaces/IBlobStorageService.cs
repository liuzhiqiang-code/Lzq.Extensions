using LzqNet.Extensions.Azure.Models;

namespace LzqNet.Extensions.Azure.Interfaces;

/// <summary>
/// Blob存储服务接口
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// 上传本地文件到Blob存储
    /// </summary>
    Task<BlobOperationResult<string>> UploadAsync(string filePath, string? blobName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传字节数组到Blob存储
    /// </summary>
    Task<BlobOperationResult<string>> UploadFromBytesAsync(byte[] content, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传流到Blob存储
    /// </summary>
    Task<BlobOperationResult<string>> UploadFromStreamAsync(Stream stream, string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载Blob到本地文件
    /// </summary>
    Task<BlobOperationResult> DownloadAsync(string blobName, string localFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载Blob到流
    /// </summary>
    Task<BlobOperationResult<Stream>> DownloadToStreamAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除Blob
    /// </summary>
    Task<BlobOperationResult<bool>> DeleteAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查Blob是否存在
    /// </summary>
    Task<BlobOperationResult<bool>> ExistsAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取Blob元数据
    /// </summary>
    Task<BlobOperationResult<BlobMetadata>> GetMetadataAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置Blob元数据
    /// </summary>
    Task<BlobOperationResult> SetMetadataAsync(string blobName, Dictionary<string, string> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取Blob访问URI（带SAS令牌）
    /// </summary>
    Task<BlobOperationResult<string>> GetBlobUriWithSasAsync(string blobName, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除Blob
    /// </summary>
    Task<BlobOperationResult<int>> BatchDeleteAsync(IEnumerable<string> blobNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出指定前缀的Blob
    /// </summary>
    IAsyncEnumerable<string> ListBlobsAsync(string? prefix = null, CancellationToken cancellationToken = default);
}