using System.ComponentModel.DataAnnotations;

namespace Lzq.Extensions.Redis;

public class LzqRedisOptions
{
    [Required]
    public string Prefix { get; set; } = "Lzq:";

    /// <summary>
    /// 连接字符串。
    /// 集群模式下可填其中一个节点，库会自动通过 CLUSTER NODES 发现全量节点。
    /// </summary>
    [Required]
    public string ConnectionString { get; set; }

    /// <summary>
    /// 哨兵节点列表。如果不为空，则自动切换为哨兵模式。
    /// </summary>
    public string[]? Sentinels { get; set; }

    /// <summary>
    /// 是否为集群模式。
    /// </summary>
    public bool IsCluster { get; set; } = false;
}