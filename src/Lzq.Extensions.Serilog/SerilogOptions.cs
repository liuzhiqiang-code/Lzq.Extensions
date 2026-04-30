using Serilog;
using Serilog.Events;

namespace Lzq.Extensions.Serilog;

/// <summary>
/// Serilog 日志配置选项，完全由使用者通过代码或配置文件控制。
/// </summary>
public class SerilogOptions
{
    // ---------- 全局设置 ----------
    /// <summary>
    /// 统一输出模板，用于控制台、文件等文本型 Sink。
    /// 设置为 null 时，控制台将使用紧凑 JSON 格式。
    /// </summary>
    public string OutputTemplate { get; set; } =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [TraceId:{TraceId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// 全局最低日志级别
    /// </summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Debug;

    // ---------- 控制台输出配置 ----------
    /// <summary>
    /// 是否启用控制台输出（默认开启）
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// 控制台是否使用异步写入（推荐开启）
    /// </summary>
    public bool ConsoleAsync { get; set; } = true;

    // ---------- 文件输出配置 ----------
    /// <summary>
    /// 是否启用文件日志输出
    /// </summary>
    public bool EnableFile { get; set; } = false;

    /// <summary>
    /// 文件日志路径（例如 "Logs/log-.txt"）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 单个日志文件大小限制（字节），默认 10MB
    /// </summary>
    public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// 保留的日志文件数量（按天滚动），默认 7 天
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 7;

    /// <summary>
    /// 文件日志滚动间隔，默认按天
    /// </summary>
    public RollingInterval FileRollingInterval { get; set; } = RollingInterval.Day;

    /// <summary>
    /// 文件输出是否使用异步写入
    /// </summary>
    public bool FileAsync { get; set; } = true;

    // ---------- SQLite 输出配置 ----------
    /// <summary>
    /// 是否启用 SQLite 日志输出
    /// </summary>
    public bool EnableSQLite { get; set; } = false;

    /// <summary>
    /// SQLite 数据库文件路径（默认 "Logs/log.db"）
    /// </summary>
    public string? SQLitePath { get; set; } = "Logs/log.db";


    // ---------- Grafana Loki 输出配置 ----------
    /// <summary>
    /// 是否启用 Grafana Loki 日志输出
    /// </summary>
    public bool EnableLoki { get; set; } = false;

    /// <summary>
    /// Loki 服务地址
    /// </summary>
    public string? LokiUrl { get; set; }

    /// <summary>
    /// Loki 中的服务名称标签
    /// </summary>
    public string LokiServiceName { get; set; } = "unknown_service";

    // ---------- 其他 Sink 可继续扩展 ----------
}