using Azure;
using Azure.Data.Tables;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace LzqNet.Extensions.Azure.Models;

// <summary>
/// 表实体基类
/// </summary>
public abstract class TableEntityBase : ITableEntity
{
    /// <summary>
    /// 分区键 - 用于数据分区，同一分区内的数据可以批量操作
    /// </summary>
    public virtual string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// 行键 - 分区内唯一标识
    /// </summary>
    public virtual string RowKey { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳（服务器端维护）
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// ETag（服务器端维护，用于乐观并发）
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    protected TableEntityBase()
    {
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    protected TableEntityBase(string partitionKey, string rowKey)
    {
        PartitionKey = partitionKey;
        RowKey = rowKey;
    }
}


/// <summary>
/// 表实体扩展方法
/// </summary>
public static class TableEntityExtensions
{
    private static readonly ConcurrentDictionary<Type, string> _tableNameCache = new();

    /// <summary>
    /// 获取实体对应的表名
    /// </summary>
    public static string GetTableName<T>(this T entity) where T : TableEntityBase
    {
        return _tableNameCache.GetOrAdd(typeof(T), type =>
        {
            var tableAttribute = type.GetCustomAttribute<TableAttribute>();
            return tableAttribute?.Name ?? type.Name;
        });
    }

    /// <summary>
    /// 获取实体类型对应的表名（静态方法）
    /// </summary>
    public static string GetTableName<T>() where T : TableEntityBase
    {
        return _tableNameCache.GetOrAdd(typeof(T), type =>
        {
            var tableAttribute = type.GetCustomAttribute<TableAttribute>();
            return tableAttribute?.Name ?? type.Name;
        });
    }

    /// <summary>
    /// 获取类型对应的表名
    /// </summary>
    public static string GetTableName(this Type type)
    {
        if (!typeof(TableEntityBase).IsAssignableFrom(type))
        {
            throw new ArgumentException($"类型 {type.Name} 不是 TableEntityBase 的子类");
        }

        return _tableNameCache.GetOrAdd(type, t =>
        {
            var tableAttribute = t.GetCustomAttribute<TableAttribute>();
            return tableAttribute?.Name ?? t.Name;
        });
    }
}