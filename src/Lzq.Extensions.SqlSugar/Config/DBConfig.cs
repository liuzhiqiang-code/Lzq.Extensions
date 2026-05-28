using DbType = SqlSugar.DbType;

namespace Lzq.Extensions.SqlSugar.Config;

/// <summary>
/// 数据库配置
/// </summary>
public class DBConfig
{
    /// <summary>
    /// 数据库标识
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    /// 数据库类型
    ///  MySql = 0, SqlServer = 1,Sqlite = 2, Oracle = 3, PostgreSQL = 4, Dm = 5,Kdbndp = 6,Oscar = 7,MySqlConnector = 8,
    ///  Access = 9,OpenGauss = 10,QuestDB = 11,HG = 12,ClickHouse = 13, GBase = 14, Odbc = 0xF, Custom = 900
    /// </summary>
    public DbType DbType { get; set; }

    /// <summary>
    /// 数据库连接串（主库）
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// 数据库超时时间
    /// </summary>
    public int CommandTimeOut { get; set; }

    /// <summary>
    /// 从库配置列表（读写分离用），不配则全走主库
    /// </summary>
    public List<SlaveDbConfig>? Slaves { get; set; }
}

/// <summary>
/// 从库数据库配置
/// </summary>
public class SlaveDbConfig
{
    /// <summary>
    /// 从库连接串
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// 负载权重（值越大被选中概率越高），默认 50
    /// </summary>
    public int HitRate { get; set; } = 50;
}