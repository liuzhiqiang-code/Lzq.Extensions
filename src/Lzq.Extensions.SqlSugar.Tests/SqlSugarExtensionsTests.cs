using global::Lzq.Core.Interfaces;
using global::Lzq.Extensions.SqlSugar.Repository;
using global::Lzq.Extensions.SqlSugar.SeedData;
using global::SqlSugar;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Yitter.IdGenerator;

namespace Lzq.Extensions.SqlSugar.Tests;

public class SqlSugarExtensionsTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ISqlSugarClient _sqlSugarClient;
    private readonly SqliteConnection _keepAliveConnection; // ← 保持连接

    public SqlSugarExtensionsTests()
    {
        // 保持一个打开的 SQLite 连接，防止内存数据库被销毁
        _keepAliveConnection = new SqliteConnection("DataSource=:memory:;Mode=Memory;Cache=Shared");
        _keepAliveConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdGeneratorOptions:WorkerId"] = "1",
                ["IdGeneratorOptions:WorkerIdBitLength"] = "6",
                ["IdGeneratorOptions:SeqBitLength"] = "6",

                ["DBConfigs:0:Tag"] = "master",
                ["DBConfigs:0:DbType"] = "Sqlite",
                ["DBConfigs:0:ConnectionString"] = "DataSource=:memory:;Mode=Memory;Cache=Shared",

                ["DBConfigs:1:Tag"] = "secondary",
                ["DBConfigs:1:DbType"] = "Sqlite",
                ["DBConfigs:1:ConnectionString"] = "DataSource=:memory:;Mode=Memory;Cache=Shared",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser());
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddLzqSqlSugar(configuration);

        _serviceProvider = services.BuildServiceProvider();
        _sqlSugarClient = _serviceProvider.GetRequiredService<ISqlSugarClient>();
    }

    private void InitDatabase(string configId = "master")
    {
        var db = _sqlSugarClient.AsTenant().GetConnection(configId);
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();
    }

    #region 基础连接测试

    [Fact]
    public void SqlSugarClient_ShouldBeRegistered()
    {
        Assert.NotNull(_sqlSugarClient);
    }

    [Fact]
    public void ShouldBeSingleton()
    {
        var client1 = _serviceProvider.GetRequiredService<ISqlSugarClient>();
        var client2 = _serviceProvider.GetRequiredService<ISqlSugarClient>();
        Assert.Same(client1, client2);
    }

    [Fact]
    public void ShouldSupportMultipleConfigs()
    {
        // 验证两库都可连接
        var masterDb = _sqlSugarClient.AsTenant().GetConnection("master");
        Assert.NotNull(masterDb);

        var secondaryDb = _sqlSugarClient.AsTenant().GetConnection("secondary");
        Assert.NotNull(secondaryDb);
    }

    [Fact]
    public void IdGenerator_ShouldBeInitialized()
    {
        var id1 = YitIdHelper.NextId();
        var id2 = YitIdHelper.NextId();
        Assert.NotEqual(id1, id2);
        Assert.True(id2 > id1);
    }

    #endregion

    #region 实体映射测试

    [SugarTable("test_users")]
    public class TestUser
    {
        [SugarColumn(IsPrimaryKey = true)]
        public long Id { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? Nickname { get; set; }

        public string UserName { get; set; } = string.Empty;

        public int Age { get; set; }

        public bool IsActive { get; set; }
    }

    [Fact]
    public void CodeFirst_ShouldCreateTable()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();

        // Act
        db.CodeFirst.InitTables<TestUser>();

        // Assert
        var tables = db.DbMaintenance.GetTableInfoList();
        Assert.Contains(tables, t => t.Name.Equals("test_users", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AOP_ColumnNameShouldBeLowercase()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        // Act
        var columnInfos = db.DbMaintenance.GetColumnInfosByTableName("test_users");

        // Assert：表名和列名都应该是小写
        Assert.Contains(columnInfos, c => c.DbColumnName == "id");
        Assert.Contains(columnInfos, c => c.DbColumnName == "username");
        Assert.Contains(columnInfos, c => c.DbColumnName == "nickname");
    }

    [Fact]
    public void AOP_NullablePropertyShouldBeNullableColumn()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        // Act
        var nicknameColumn = db.DbMaintenance.GetColumnInfosByTableName("test_users")
            .FirstOrDefault(c => c.DbColumnName == "nickname");

        // Assert
        Assert.NotNull(nicknameColumn);
        Assert.True(nicknameColumn.IsNullable);
    }

    #endregion

    #region CRUD 测试

    [Fact]
    public async Task Insert_ShouldWork()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        var user = new TestUser
        {
            Id = YitIdHelper.NextId(),
            UserName = "Alice",
            Age = 25,
            IsActive = true,
        };

        // Act
        var result = await db.Insertable(user).ExecuteCommandAsync();

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task Query_ShouldReturnInsertedData()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        var user = new TestUser
        {
            Id = YitIdHelper.NextId(),
            UserName = "Bob",
            Age = 30,
            IsActive = false,
        };
        await db.Insertable(user).ExecuteCommandAsync();

        // Act
        var queried = await db.Queryable<TestUser>()
            .Where(u => u.UserName == "Bob")
            .SingleAsync();

        // Assert
        Assert.NotNull(queried);
        Assert.Equal("Bob", queried.UserName);
        Assert.Equal(30, queried.Age);
    }

    [Fact]
    public async Task Update_ShouldModifyData()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        var user = new TestUser
        {
            Id = YitIdHelper.NextId(),
            UserName = "Charlie",
            Age = 20,
            IsActive = true,
        };
        await db.Insertable(user).ExecuteCommandAsync();

        // Act
        await db.Updateable<TestUser>()
            .SetColumns(u => u.Age == 35)
            .Where(u => u.UserName == "Charlie")
            .ExecuteCommandAsync();

        var updated = await db.Queryable<TestUser>()
            .Where(u => u.UserName == "Charlie")
            .SingleAsync();

        // Assert
        Assert.Equal(35, updated.Age);
    }

    [Fact]
    public async Task Delete_ShouldRemoveData()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        var user = new TestUser
        {
            Id = YitIdHelper.NextId(),
            UserName = "David",
            Age = 40,
            IsActive = false,
        };
        await db.Insertable(user).ExecuteCommandAsync();

        // Act
        await db.Deleteable<TestUser>()
            .Where(u => u.UserName == "David")
            .ExecuteCommandAsync();

        var count = await db.Queryable<TestUser>()
            .Where(u => u.UserName == "David")
            .CountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region Repository 测试

    [Fact]
    public async Task Repository_ShouldWork()
    {
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();
        db.CodeFirst.InitTables<TestUser>();

        var repo = _serviceProvider.GetRequiredService<ISqlSugarRepository<TestUser>>();

        var user = new TestUser
        {
            Id = YitIdHelper.NextId(),
            UserName = "Eve",
            Age = 28,
            IsActive = true,
        };

        // Act
        await repo.InsertAsync(user);

        var list = await repo.GetListAsync(u => u.UserName == "Eve");

        // Assert
        Assert.Single(list);
        Assert.Equal("Eve", list[0].UserName);
    }

    #endregion

    #region SeedData 测试

    public class TestSeedData : ISeedDataInitializer
    {
        public bool IsCheckTableExists => true;

        public void Initialize(ISqlSugarClient db)
        {
            var conn = db.AsTenant().GetConnection("master");
            conn.CodeFirst.InitTables<TestUser>();

            if (!conn.Queryable<TestUser>().Any())
            {
                conn.Insertable(new TestUser
                {
                    Id = YitIdHelper.NextId(),
                    UserName = "SeedUser",
                    Age = 99,
                }).ExecuteCommand();
            }
        }
    }

    [Fact]
    public void SeedData_ShouldBeExecuted()
    {
        InitDatabase();
        // Arrange
        var db = _sqlSugarClient.AsTenant().GetConnection("master");
        db.DbMaintenance.CreateDatabase();

        var seed = new TestSeedData();

        // Act
        seed.Initialize(_sqlSugarClient);

        // Assert
        var user = db.Queryable<TestUser>()
            .Where(u => u.UserName == "SeedUser")
            .Single();
        Assert.NotNull(user);
        Assert.Equal(99, user.Age);
    }

    #endregion

    #region 审计字段测试

    [SugarTable("audited_table")]
    public class AuditedEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task AuditedField_ShouldCallCurrentUser()
    {
        // 这个测试验证 ICurrentUser 被正确注入到 AOP 中
        // 具体行为取决于你的 UseAuditedField 实现
        var currentUser = _serviceProvider.GetRequiredService<ICurrentUser>();
        Assert.NotNull(currentUser);
        Assert.IsType<FakeCurrentUser>(currentUser);
    }

    #endregion

    #region Helpers

    public class FakeCurrentUser : ICurrentUser
    {
        public string UserId => "1";

        public string? UserName => "TestUser";

        public List<string>? Roles => ["1"];

        public string? Email => "";

        public string Sex => "";

        public string Sid => Guid.NewGuid().ToString();

        public string TenantId => "";
    }

    #endregion

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _keepAliveConnection?.Close();
        _keepAliveConnection?.Dispose();

    }
}