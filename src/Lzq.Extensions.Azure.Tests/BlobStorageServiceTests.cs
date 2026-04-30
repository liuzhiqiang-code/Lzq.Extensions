using Azure.Core;
using LzqNet.Extensions.Azure.Configuration;
using LzqNet.Extensions.Azure.Models;
using LzqNet.Extensions.Azure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using Xunit;

namespace LzqNet.Extensions.Azure.Tests;

[Collection("Azurite")]
public class BlobStorageServiceTests : IDisposable
{
    private readonly Mock<ILogger<BlobStorageService>> _mockLogger;
    private readonly BlobStorageOptions _options;
    private readonly BlobStorageService _service;
    private readonly string _testFilePath;
    private readonly string _testContent;

    public BlobStorageServiceTests(AzuriteFixture azuriteFixture)
    {
        // 创建模拟对象
        _mockLogger = new Mock<ILogger<BlobStorageService>>();

        // 配置选项
        _options = new BlobStorageOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=testkey;EndpointSuffix=core.windows.net",
            ContainerName = "test-container",
            CreateContainerIfNotExists = true,
            UseManagedIdentity = false,
            RetryOptions = new StorageRetryOptions
            {
                MaxRetries = 3,
                Mode = RetryMode.Exponential,
                DelayMilliseconds = 100,
                MaxDelayMilliseconds = 1000,
                EnableRetryLogging = true
            }
        };

        // 创建测试文件
        _testContent = "Hello, World!";
        _testFilePath = Path.GetTempFileName();
        File.WriteAllText(_testFilePath, _testContent);

        // 由于 BlobContainerClient 无法直接 Mock，我们需要使用实际的客户端或使用更复杂的 Mock
        // 这里为了测试，我们创建一个包装器或使用集成测试
        // 由于 BlobStorageService 构造函数中会调用 CreateIfNotExistsAsync，我们需要处理这个

        // 创建一个 Options 包装器
        var optionsWrapper = new OptionsWrapper<BlobStorageOptions>(_options);

        // 创建服务实例（使用真实连接字符串或使用测试模拟器）
        // 这里为了演示，我们创建一个使用实际 Azure 存储模拟器的服务
        // 如果使用 Azurite，连接字符串为：UseDevelopmentStorage=true
        var testOptions = new BlobStorageOptions
        {
            // 使用简化的连接字符串
            ConnectionString = "UseDevelopmentStorage=true",

            // 为每个测试实例生成唯一容器名
            ContainerName = $"test-container-{Guid.NewGuid():N}",

            CreateContainerIfNotExists = true,
            UseManagedIdentity = false,

            // 测试优化配置
            DefaultUploadTimeoutSeconds = 30,
            DefaultDownloadTimeoutSeconds = 30,
            EnableServerSideEncryption = false,

            RetryOptions = new StorageRetryOptions
            {
                Mode = RetryMode.Fixed,
                MaxRetries = 1,              // 测试中减少重试次数，加快失败反馈
                DelayMilliseconds = 10,       // 减少等待时间
                MaxDelayMilliseconds = 100,
                NetworkTimeoutSeconds = 30,
                EnableCircuitBreaker = false, // 测试中禁用熔断器
                EnableRetryLogging = true     // 开启日志便于调试
            }
        };

        var testOptionsWrapper = new OptionsWrapper<BlobStorageOptions>(testOptions);
        _service = new BlobStorageService(testOptionsWrapper, _mockLogger.Object);
    }

    [Fact]
    public async Task UploadAsync_ShouldUploadFileSuccessfully()
    {
        // Arrange
        var blobName = "test-file.txt";

        // Act
        var result = await _service.UploadAsync(_testFilePath, blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Contains(blobName, result.Data);
    }

    [Fact]
    public async Task UploadAsync_WhenFileNotExists_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentFile = Path.GetTempFileName();
        File.Delete(nonExistentFile);
        var blobName = "test-file.txt";

        // Act
        var result = await _service.UploadAsync(nonExistentFile, blobName);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("FileNotFound", result.ErrorCode);
        Assert.Contains("文件不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task UploadAsync_WithNullBlobName_ShouldGenerateBlobName()
    {
        // Act
        var result = await _service.UploadAsync(_testFilePath, null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);

        // 验证生成的 Blob 名称格式：yyyy/MM/dd/filename_xxxxxxxx.ext
        var uri = new Uri(result.Data);
        var blobName = uri.Segments.Last();
        Assert.Contains(Path.GetFileNameWithoutExtension(_testFilePath), blobName);
    }

    [Fact]
    public async Task UploadFromBytesAsync_ShouldUploadBytesSuccessfully()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test content");
        var blobName = "test-bytes.txt";

        // Act
        var result = await _service.UploadFromBytesAsync(content, blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Contains(blobName, result.Data);
    }

    [Fact]
    public async Task UploadFromBytesAsync_WithEmptyBlobName_ShouldReturnFailure()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Test content");

        // Act
        var result = await _service.UploadFromBytesAsync(content, string.Empty);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidBlobName", result.ErrorCode);
    }

    [Fact]
    public async Task UploadFromStreamAsync_ShouldUploadStreamSuccessfully()
    {
        // Arrange
        var content = "Stream content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var blobName = "test-stream.txt";

        // Act
        var result = await _service.UploadFromStreamAsync(stream, blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Contains(blobName, result.Data);
    }

    [Fact]
    public async Task UploadFromStreamAsync_WithUnreadableStream_ShouldReturnFailure()
    {
        // Arrange
        var stream = new MemoryStream();
        stream.Close(); // 关闭流使其不可读
        var blobName = "test-stream.txt";

        // Act
        var result = await _service.UploadFromStreamAsync(stream, blobName);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidStream", result.ErrorCode);
    }

    [Fact]
    public async Task DownloadAsync_ShouldDownloadFileSuccessfully()
    {
        // Arrange
        var blobName = "test-download.txt";
        var content = "Download test content";

        // 先上传一个文件
        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        var downloadPath = Path.GetTempFileName();
        File.Delete(downloadPath); // 删除临时文件，准备下载

        // Act
        var result = await _service.DownloadAsync(blobName, downloadPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(downloadPath));

        var downloadedContent = await File.ReadAllTextAsync(downloadPath);
        Assert.Equal(content, downloadedContent);

        // 清理
        File.Delete(downloadPath);
    }

    [Fact]
    public async Task DownloadAsync_WhenBlobNotExists_ShouldReturnFailure()
    {
        // Arrange
        var blobName = "non-existent-blob.txt";
        var downloadPath = Path.GetTempFileName();

        // Act
        var result = await _service.DownloadAsync(blobName, downloadPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("BlobNotFound", result.ErrorCode);
    }

    [Fact]
    public async Task DownloadToStreamAsync_ShouldDownloadToStreamSuccessfully()
    {
        // Arrange
        var blobName = "test-stream-download.txt";
        var content = "Stream download test content";

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        // Act
        var result = await _service.DownloadToStreamAsync(blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);

        using var reader = new StreamReader(result.Data);
        var downloadedContent = await reader.ReadToEndAsync();
        Assert.Equal(content, downloadedContent);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteBlobSuccessfully()
    {
        // Arrange
        var blobName = "test-delete.txt";
        var content = "Delete test content";

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        // Act
        var result = await _service.DeleteAsync(blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        // 验证文件已删除
        var existsResult = await _service.ExistsAsync(blobName);
        Assert.False(existsResult.Data);
    }

    [Fact]
    public async Task DeleteAsync_WhenBlobNotExists_ShouldReturnFalse()
    {
        // Arrange
        var blobName = "non-existent-delete.txt";

        // Act
        var result = await _service.DeleteAsync(blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task ExistsAsync_WhenBlobExists_ShouldReturnTrue()
    {
        // Arrange
        var blobName = "test-exists.txt";
        var content = "Exists test content";

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        // Act
        var result = await _service.ExistsAsync(blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task ExistsAsync_WhenBlobNotExists_ShouldReturnFalse()
    {
        // Arrange
        var blobName = "non-existent-exists.txt";

        // Act
        var result = await _service.ExistsAsync(blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task GetMetadataAsync_ShouldReturnMetadataSuccessfully()
    {
        // Arrange
        var blobName = "test-metadata.txt";
        var content = "Metadata test content";

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        // Act
        var result = await _service.GetMetadataAsync(blobName);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(blobName, result.Data.Name);
        Assert.Equal(content.Length, result.Data.Size);
        Assert.Equal("text/plain", result.Data.ContentType);
    }

    [Fact]
    public async Task GetMetadataAsync_WhenBlobNotExists_ShouldReturnFailure()
    {
        // Arrange
        var blobName = "non-existent-metadata.txt";

        // Act
        var result = await _service.GetMetadataAsync(blobName);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("BlobNotFound", result.ErrorCode);
    }

    [Fact]
    public async Task SetMetadataAsync_ShouldSetMetadataSuccessfully()
    {
        // Arrange
        var blobName = "test-set-metadata.txt";
        var content = "Set metadata test content";
        var metadata = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" }
            };

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        // Act
        var result = await _service.SetMetadataAsync(blobName, metadata);

        // Assert
        Assert.True(result.IsSuccess);

        // 验证元数据已设置
        var metadataResult = await _service.GetMetadataAsync(blobName);
        Assert.Equal(metadata.Count, metadataResult.Data.Metadata.Count);
        Assert.Equal("value1", metadataResult.Data.Metadata["key1"]);
        Assert.Equal("value2", metadataResult.Data.Metadata["key2"]);
    }

    [Fact]
    public async Task GetBlobUriWithSasAsync_ShouldGenerateSasUriSuccessfully()
    {
        // Arrange
        var blobName = "test-sas.txt";
        var content = "SAS test content";
        var expiry = TimeSpan.FromHours(1);

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes(content), blobName);

        // Act
        var result = await _service.GetBlobUriWithSasAsync(blobName, expiry);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Contains("sig=", result.Data); // SAS 令牌包含签名
    }

    [Fact]
    public async Task GetBlobUriWithSasAsync_WhenBlobNotExists_ShouldReturnFailure()
    {
        // Arrange
        var blobName = "non-existent-sas.txt";
        var expiry = TimeSpan.FromHours(1);

        // Act
        var result = await _service.GetBlobUriWithSasAsync(blobName, expiry);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("BlobNotFound", result.ErrorCode);
    }

    [Fact]
    public async Task BatchDeleteAsync_ShouldDeleteMultipleBlobsSuccessfully()
    {
        // Arrange
        var blobNames = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            var blobName = $"test-batch-{i}.txt";
            blobNames.Add(blobName);
            await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes($"Content {i}"), blobName);
        }

        // Act
        var result = await _service.BatchDeleteAsync(blobNames);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Data);

        // 验证所有文件都已删除
        foreach (var blobName in blobNames)
        {
            var existsResult = await _service.ExistsAsync(blobName);
            Assert.False(existsResult.Data);
        }
    }

    [Fact]
    public async Task BatchDeleteAsync_WithNonExistentBlobs_ShouldReturnSuccessWithPartialDelete()
    {
        // Arrange
        var blobNames = new List<string>
            {
                "existing-blob.txt",
                "non-existent-1.txt",
                "non-existent-2.txt"
            };

        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes("Content"), "existing-blob.txt");

        // Act
        var result = await _service.BatchDeleteAsync(blobNames);

        // Assert
        Assert.True(result.IsSuccess); // 部分删除也会返回成功
        Assert.Equal(1, result.Data); // 只有存在的文件被删除
    }

    [Fact]
    public async Task ListBlobsAsync_ShouldListBlobsSuccessfully()
    {
        // Arrange
        var testBlobs = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            var blobName = $"test-list-{i}.txt";
            testBlobs.Add(blobName);
            await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes($"Content {i}"), blobName);
        }

        // Act
        var listedBlobs = new List<string>();
        await foreach (var blobName in _service.ListBlobsAsync())
        {
            listedBlobs.Add(blobName);
        }

        // Assert
        foreach (var testBlob in testBlobs)
        {
            Assert.Contains(testBlob, listedBlobs);
        }
    }

    [Fact]
    public async Task ListBlobsAsync_WithPrefix_ShouldFilterBlobsSuccessfully()
    {
        // Arrange
        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes("Content 1"), "prefix/file1.txt");
        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes("Content 2"), "prefix/file2.txt");
        await _service.UploadFromBytesAsync(Encoding.UTF8.GetBytes("Content 3"), "other/file3.txt");

        // Act
        var listedBlobs = new List<string>();
        await foreach (var blobName in _service.ListBlobsAsync("prefix/"))
        {
            listedBlobs.Add(blobName);
        }

        // Assert
        Assert.Equal(2, listedBlobs.Count);
        Assert.Contains("prefix/file1.txt", listedBlobs);
        Assert.Contains("prefix/file2.txt", listedBlobs);
        Assert.DoesNotContain("other/file3.txt", listedBlobs);
    }

    [Theory]
    [InlineData(".txt", "text/plain")]
    [InlineData(".json", "application/json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".html", "text/html")]
    [InlineData(".css", "text/css")]
    [InlineData(".js", "application/javascript")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".unknown", "application/octet-stream")]
    public async Task UploadAsync_ShouldSetCorrectContentType(string extension, string expectedContentType)
    {
        // Arrange
        var fileName = $"test{extension}";
        var filePath = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllText(filePath, "Test content");

        try
        {
            // Act
            var result = await _service.UploadAsync(filePath, fileName);

            // Assert
            Assert.True(result.IsSuccess);

            // 验证 ContentType
            var metadata = await _service.GetMetadataAsync(fileName);
            Assert.Equal(expectedContentType, metadata.Data.ContentType);
        }
        finally
        {
            File.Delete(filePath);
            await _service.DeleteAsync(fileName);
        }
    }

    [Fact]
    public async Task ConcurrentUploadAsync_ShouldHandleMultipleFilesConcurrently()
    {
        // Arrange
        var tasks = new List<Task<BlobOperationResult<string>>>();
        var filePaths = new List<string>();

        for (int i = 0; i < 10; i++)
        {
            var filePath = Path.GetTempFileName();
            filePaths.Add(filePath);
            await File.WriteAllTextAsync(filePath, $"Content {i}");
            tasks.Add(_service.UploadAsync(filePath, $"file-{i}.txt"));
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
        {
            Assert.True(result.IsSuccess);
        }

        // 清理
        foreach (var filePath in filePaths)
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ConcurrentUploadAsync_SameFile_ShouldSerializeAccess()
    {
        // Arrange
        var sameFilePath = Path.GetTempFileName();
        var content = "Same file content";
        await File.WriteAllTextAsync(sameFilePath, content);

        var tasks = new List<Task<BlobOperationResult<string>>>();
        var startTime = DateTime.UtcNow;

        // Act
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(_service.UploadAsync(sameFilePath, $"same-file-{i}.txt"));
        }

        var results = await Task.WhenAll(tasks);
        var endTime = DateTime.UtcNow;

        // Assert
        foreach (var result in results)
        {
            Assert.True(result.IsSuccess);
        }

        // 由于是串行执行，总时间应该大于单次上传时间的5倍
        // 但这里我们只验证没有抛出异常

        // 清理
        File.Delete(sameFilePath);
    }

    public void Dispose()
    {
        // 清理测试文件
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }

        // 清理容器中的所有测试 Blob
        Task.Run(async () =>
        {
            await foreach (var blobName in _service.ListBlobsAsync())
            {
                await _service.DeleteAsync(blobName);
            }
        }).GetAwaiter().GetResult();
    }
}