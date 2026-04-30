using Azure;
using Azure.Core;
using Azure.Data.Tables;
using LzqNet.Extensions.Azure.Configuration;
using LzqNet.Extensions.Azure.Models;
using LzqNet.Extensions.Azure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LzqNet.Extensions.Azure.Tests;

/// <summary>
/// Table 存储服务集成测试 (基于 Azurite)
/// </summary>
[Collection("Azurite")]
public class TableStorageServiceTests : IDisposable
{
    private readonly Mock<ILogger<TableStorageService>> _mockLogger;
    private readonly TableStorageOptions _options;
    private readonly TableStorageService _service;
    private readonly string _tableName;

    public TableStorageServiceTests()
    {
        _mockLogger = new Mock<ILogger<TableStorageService>>();

        // 核心：模拟你的表名获取逻辑
        _tableName = typeof(TestLogEntity).GetTableName();

        _options = new TableStorageOptions
        {
            // 使用本地 Azurite 默认连接字符串
            ConnectionString = "UseDevelopmentStorage=true",
            CreateTableIfNotExists = true,
            UseManagedIdentity = false,
            RetryOptions = new StorageRetryOptions
            {
                MaxRetries = 2,
                Mode = RetryMode.Exponential,
                DelayMilliseconds = 10,
                MaxDelayMilliseconds = 100,
                NetworkTimeoutSeconds = 30,
                EnableCircuitBreaker = false, // 测试中默认禁用，避免干扰其他用例
                EnableRetryLogging = true
            }
        };

        var optionsWrapper = new OptionsWrapper<TableStorageOptions>(_options);
        _service = new TableStorageService(optionsWrapper, _mockLogger.Object);

        // 确保测试前环境初始化
        EnsureTableCleaned();
    }

    private void EnsureTableCleaned()
    {
        var client = new TableServiceClient(_options.ConnectionString);
        var tableClient = client.GetTableClient(_tableName);
        tableClient.CreateIfNotExists();
    }

    /// <summary>
    /// 测试用实体类
    /// </summary>
    public class TestLogEntity : TableEntityBase
    {
        public string? Message { get; set; }
        public int Level { get; set; }
        public bool IsProcessed { get; set; }
    }

    #region 1. 基础 CRUD 操作测试

    [Fact]
    public async Task InsertAsync_ShouldTargetCorrectTableAndSaveData()
    {
        // Arrange
        var entity = new TestLogEntity { PartitionKey = "Log", RowKey = Guid.NewGuid().ToString(), Message = "Insert Test", Level = 1 };

        // Act
        var result = await _service.InsertAsync(entity);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);

        // 验证物理表数据
        var getResult = await _service.GetAsync<TestLogEntity>(entity.PartitionKey, entity.RowKey);
        Assert.Equal("Insert Test", getResult.Data?.Message);
    }

    [Fact]
    public async Task UpdateAsync_WithETagMismatch_ShouldReturnFailure()
    {
        // Arrange
        var entity = new TestLogEntity { PartitionKey = "ETag", RowKey = "1", Message = "V1" };
        await _service.InsertAsync(entity);

        // 修改 ETag 模拟并发冲突
        entity.ETag = new ETag("W/\"FakeETag\"");

        // Act
        var result = await _service.UpdateAsync(entity);

        // Assert
        Assert.False(result.IsSuccess);
        // Azure 会返回 412 Precondition Failed
        Assert.NotNull(result.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_WhenEntityDoesNotExist_ShouldReturnEntityNotFound()
    {
        // Act
        var result = await _service.GetAsync<TestLogEntity>("Non", "Existent");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("EntityNotFound", result.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveEntitySuccessfully()
    {
        // Arrange
        var pk = "Del";
        var rk = "1";
        await _service.InsertAsync(new TestLogEntity { PartitionKey = pk, RowKey = rk });

        // Act
        var result = await _service.DeleteAsync<TestLogEntity>(pk, rk);

        // Assert
        Assert.True(result.IsSuccess);
        var check = await _service.GetAsync<TestLogEntity>(pk, rk);
        Assert.False(check.IsSuccess);
    }

    #endregion

    #region 2. 批量操作测试 (验证 Chunk 100 逻辑)

    [Fact]
    public async Task BatchUpsertAsync_WhenEntitiesCrossPartitions_ShouldProcessAllGroups()
    {
        // Arrange: 准备 110 条数据（跨分区且超过单次事务上限）
        var entities = new List<TestLogEntity>();
        // 分区 A: 105 条 (触发 100+5 的分块)
        entities.AddRange(Enumerable.Range(1, 105).Select(i => new TestLogEntity { PartitionKey = "PartA", RowKey = i.ToString() }));
        // 分区 B: 5 条
        entities.AddRange(Enumerable.Range(1, 5).Select(i => new TestLogEntity { PartitionKey = "PartB", RowKey = i.ToString() }));

        // Act
        var result = await _service.BatchUpsertAsync(entities);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(110, result.Data);

        // 抽样检查
        var checkA = await _service.GetAsync<TestLogEntity>("PartA", "105");
        var checkB = await _service.GetAsync<TestLogEntity>("PartB", "5");
        Assert.True(checkA.IsSuccess);
        Assert.True(checkB.IsSuccess);
    }

    #endregion

    #region 3. 查询操作测试 (验证 BuildFilter 逻辑)

    [Fact]
    public async Task QueryAsync_WithPartitionAndFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var pk = "QueryTest";
        // 清理旧数据或确保环境隔离
        await _service.InsertAsync(new TestLogEntity { PartitionKey = pk, RowKey = "1", Level = 10 });
        await _service.InsertAsync(new TestLogEntity { PartitionKey = pk, RowKey = "2", Level = 20 });
        await _service.InsertAsync(new TestLogEntity { PartitionKey = "Other", RowKey = "1", Level = 20 });

        var options = new TableQueryOptions
        {
            PartitionKey = pk,
            Filter = "Level gt 15"
        };

        // Act
        var result = await _service.QueryAllAsync<TestLogEntity>(options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data);
        Assert.Equal("2", result.Data![0].RowKey);
    }

    [Fact]
    public async Task QueryAsync_WithSelectColumns_ShouldOnlyFillSelectedProperties()
    {
        // Arrange
        var pk = "SelectTest";
        await _service.InsertAsync(new TestLogEntity { PartitionKey = pk, RowKey = "1", Message = "Secret", Level = 99 });

        var options = new TableQueryOptions
        {
            PartitionKey = pk,
            SelectColumns = new List<string> { "PartitionKey", "RowKey", "Level" } // 排除 Message
        };

        // Act
        var results = await _service.QueryAllAsync<TestLogEntity>(options);

        // Assert
        var entity = results.Data.First();
        Assert.Equal(99, entity.Level);
        Assert.Null(entity.Message); // 未选择的字段应为 null
    }

    #endregion

    #region 4. 管理与故障策略测试

    [Fact]
    public async Task TableExistsAsync_ShouldReturnTrueIfTableExists()
    {
        // Act
        var result = await _service.TableExistsAsync<TestLogEntity>();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task ExecuteWithPolicy_ShouldLogRetry_OnTransientFailure()
    {
        // Arrange
        // AccountKey 必须是合法的 Base64 字符串 (例如 d3Jvbmc=)
        // AccountName 随意填一个不存在的，这样在尝试连接时会触发 RequestFailedException (Status >= 500 或 429 等)
        var badOptions = new TableStorageOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=nonexistentservicetesting;AccountKey=d3Jvbmc=;EndpointSuffix=core.windows.net",
            RetryOptions = new StorageRetryOptions
            {
                MaxRetries = 1,
                EnableRetryLogging = true,
                DelayMilliseconds = 1 // 缩短延迟，加快测试速度
            }
        };

        var badService = new TableStorageService(
            new OptionsWrapper<TableStorageOptions>(badOptions),
            _mockLogger.Object);

        // Act
        // 注意：这里由于连接字符串格式正确但指向不存在的服务，
        // SDK 会尝试请求并触发 RequestFailedException，从而进入你写的 Polly 策略。
        await badService.GetAsync<TestLogEntity>("pk", "rk");

        // Assert: 验证是否调用了 Logger 里的 "Table 重试"
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Table 重试")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    public void Dispose()
    {
        // 清理测试生成的表
        try
        {
            var client = new TableServiceClient(_options.ConnectionString);
            client.DeleteTable(_tableName);
        }
        catch { }
    }
}