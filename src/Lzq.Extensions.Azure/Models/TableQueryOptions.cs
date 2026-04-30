namespace LzqNet.Extensions.Azure.Models;

/// <summary>
/// 表查询选项
/// </summary>
public class TableQueryOptions
{
    /// <summary>
    /// 分区键筛选（精确匹配）
    /// </summary>
    public string? PartitionKey { get; set; }

    /// <summary>
    /// 行键筛选（精确匹配）
    /// </summary>
    public string? RowKey { get; set; }

    /// <summary>
    /// 自定义筛选条件（OData 格式）
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// 选择要返回的列
    /// </summary>
    public List<string>? SelectColumns { get; set; }

    /// <summary>
    /// 最大返回条数
    /// </summary>
    public int? MaxPerPage { get; set; }

    /// <summary>
    /// 是否异步查询
    /// </summary>
    public bool Async { get; set; } = true;
}