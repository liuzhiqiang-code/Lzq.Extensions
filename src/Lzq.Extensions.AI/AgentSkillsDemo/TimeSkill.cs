using Lzq.Extensions.AI.AgentSkills;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Text.Json;

namespace Lzq.Extensions.AI.AgentSkillsDemo;

/// <summary>
/// 时间感知技能 —— 提供高精度时区转换、时间计算及标准化时间戳服务。
/// </summary>
[GeneralSkill(SkillCategory.Core, true)]
public class TimeSkill : LzqAgentSkillBase<TimeSkill>
{
    protected override string SkillName => "time-master";
    protected override string SkillDescription => "提供获取当前时间、多时区转换及复杂时间推算的能力。适用于需要处理不同地区时间或计算时间差的任务。";

    protected override string Instructions => """
        你是一个拥有全球视角的时间助手。当用户询问时间或涉及时间计算时：
        1. 使用 GetCurrentTime 获取 UTC 及关键时区（如北京、纽约、伦敦）的标准时间。
        2. 若涉及跨时区转换，调用 ConvertTime 传入明确的时区标识。
        3. 若涉及相对时间计算（如“3小时后”、“下周五”），调用 CalculateTime，并使用自然语言描述。
        4. 始终以 ISO 8601 或用户指定的易读格式输出。
        """;

    // ==================== 脚本：获取当前全球时间 ====================
    [AgentSkillScript("GetCurrentTime")]
    [Description("获取当前 UTC 时间及全球重点城市时间，用于校准上下文。")]
    public string GetCurrentTime()
    {
        var now = DateTime.UtcNow;
        var zones = new Dictionary<string, string>
        {
            { "UTC", "UTC" },
            { "China Standard Time", "北京时间 (UTC+8)" },
            { "Eastern Standard Time", "纽约时间 (UTC-5/4)" },
            { "GMT Standard Time", "伦敦时间 (UTC+0/1)" }
        };

        var results = zones.ToDictionary(
            z => z.Value,
            z => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, z.Key).ToString("yyyy-MM-dd HH:mm:ss")
        );

        return JsonSerializer.Serialize(new { CurrentUtc = now.ToString("O"), LocalTimes = results });
    }

    // ==================== 脚本：多时区转换 ====================
    [AgentSkillScript("ConvertTime")]
    [Description("将特定时间从一个时区转换为另一个时区。")]
    public string ConvertTime(
        [Description("原始时间，格式如 '2026-05-22 10:00:00'")] string timeString,
        [Description("目标时区 ID，如 'China Standard Time' 或 'Pacific Standard Time'")] string targetTimeZone)
    {
        try
        {
            var dt = DateTime.Parse(timeString);
            var targetZone = TimeZoneInfo.FindSystemTimeZoneById(targetTimeZone);
            var converted = TimeZoneInfo.ConvertTime(dt, targetZone);
            return $"时间 {timeString} 在 {targetTimeZone} 为: {converted:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            return $"转换失败: {ex.Message}，请确保时区 ID 正确。";
        }
    }

    // ==================== 脚本：时间推算 ====================
    [AgentSkillScript("CalculateTime")]
    [Description("根据自然语言描述计算未来的时间，例如 '3 days from now'。")]
    public string CalculateTime(
        [Description("时间偏移描述，如 '2 hours later', '1 week from now'")] string offsetDescription)
    {
        // 简单模拟逻辑：在实际场景中可对接更强大的解析库，如 NodaTime 或 Chrono
        var now = DateTime.Now;
        string result = offsetDescription.ToLower() switch
        {
            var s when s.Contains("hour") => now.AddHours(ExtractNumber(s)).ToString("G"),
            var s when s.Contains("day") => now.AddDays(ExtractNumber(s)).ToString("G"),
            var s when s.Contains("week") => now.AddDays(ExtractNumber(s) * 7).ToString("G"),
            _ => "无法解析的时间偏移量"
        };

        return $"计算结果: {result}";
    }

    private static double ExtractNumber(string input)
    {
        var match = System.Text.RegularExpressions.Regex.Match(input, @"\d+");
        return match.Success ? double.Parse(match.Value) : 1;
    }
}