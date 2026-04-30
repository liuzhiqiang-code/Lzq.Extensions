using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

namespace Lzq.Extensions.Serilog;

public static class SerilogExtensions
{
    public static void AddLzqSerilog(this WebApplicationBuilder builder, Action<SerilogOptions>? configureOptions = null)
    {
        // 1. 注册必要服务
        builder.Services.AddHttpContextAccessor();

        // 2. 构建选项实例
        var options = new SerilogOptions();
        configureOptions?.Invoke(options);

        // 3. 配置 Serilog
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With<ActivityTraceIdEnricher>()
            .Enrich.With<HttpRequestEnricher>();

        // ---------- 控制台输出 ----------
        if (options.EnableConsole)
        {
            if (options.ConsoleAsync)
            {
                loggerConfig.WriteTo.Async(a =>
                {
                    if (!string.IsNullOrEmpty(options.OutputTemplate))
                        a.Console(outputTemplate: options.OutputTemplate);
                    else
                        a.Console(new CompactJsonFormatter());
                });
            }
            else
            {
                if (!string.IsNullOrEmpty(options.OutputTemplate))
                    loggerConfig.WriteTo.Console(outputTemplate: options.OutputTemplate);
                else
                    loggerConfig.WriteTo.Console(new CompactJsonFormatter());
            }
        }

        // ---------- 文件输出 ----------
        if (options.EnableFile && !string.IsNullOrEmpty(options.FilePath))
        {
            if (options.FileAsync)
            {
                loggerConfig.WriteTo.Async(a => a.File(
                    path: options.FilePath,
                    rollingInterval: options.FileRollingInterval,
                    restrictedToMinimumLevel: LogEventLevel.Debug, // 文件建议记录 Debug 及以上
                    outputTemplate: options.OutputTemplate,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    retainedFileCountLimit: options.RetainedFileCountLimit));
            }
            else
            {
                loggerConfig.WriteTo.File(
                    path: options.FilePath,
                    rollingInterval: options.FileRollingInterval,
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    outputTemplate: options.OutputTemplate,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    retainedFileCountLimit: options.RetainedFileCountLimit);
            }
        }

        // ---------- SQLite 输出 ----------
        if (options.EnableSQLite && !string.IsNullOrEmpty(options.SQLitePath))
        {
            loggerConfig.WriteTo.SQLite(
                sqliteDbPath: options.SQLitePath,
                restrictedToMinimumLevel: options.MinimumLevel);
        }

        // ---------- Loki 输出 ----------
        if (options.EnableLoki && !string.IsNullOrEmpty(options.LokiUrl))
        {
            loggerConfig.WriteTo.GrafanaLoki(
                options.LokiUrl,
                labels: [new LokiLabel { Key = "service_name", Value = options.LokiServiceName }]);
        }

        // 4. 设置全局 Logger 并集成到 Host
        Log.Logger = loggerConfig.CreateLogger();
        builder.Host.UseSerilog();
    }
}