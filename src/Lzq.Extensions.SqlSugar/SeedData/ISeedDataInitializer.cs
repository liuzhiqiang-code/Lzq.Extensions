using SqlSugar;

namespace Lzq.Extensions.SqlSugar.SeedData;

/// <summary>
/// 非泛型种子数据初始化器，供容器批量解析
/// </summary>
public interface ISeedDataInitializer
{
    void Initialize(ISqlSugarClient db);
}