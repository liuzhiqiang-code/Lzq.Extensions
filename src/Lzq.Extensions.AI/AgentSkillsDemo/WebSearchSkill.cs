using Lzq.Extensions.AI.AgentSkills;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lzq.Extensions.AI.AgentSkillsDemo;

/// <summary>
/// 网络搜索技能 —— 提供搜索引擎查询和网页内容抓取能力。
/// </summary>
[GeneralSkill]
public class WebSearchSkill : LzqAgentSkillBase<WebSearchSkill>
{
    // 搜索引擎模板（只读，无需修改）
    private static readonly Dictionary<string, string> SearchTemplates = new()
    {
        { "https://cn.bing.com/search?q={0}", "必应搜索" },
        { "https://www.baidu.com/s?wd={0}&ie=utf-8&rn=10", "百度搜索" },
        { "https://kaifa.baidu.com/searchPage?wd={0}&ie=utf-8&rn=10", "百度开发搜索" },
        { "https://www.sogou.com/web?query={0}", "搜狗搜索" },
        { "https://www.so.com/s?q={0}", "360搜索" },
    };

    // ==================== 技能元数据 ====================
    protected override string SkillName => "web-search";
    protected override string SkillDescription => "提供互联网搜索引擎查询和任意网页内容抓取能力。当用户需要实时信息、资料查找或获取网页内容时使用。";

    // ==================== 核心指令 ====================
    protected override string Instructions => """
        你是一个网络搜索助手。当用户需要查找互联网上的实时信息时，请按以下指引操作：
        1. 调用 Search 脚本，传入用户的问题或关键词，脚本会自动从多个搜索引擎获取结果并清洗。
        2. 若需要查看某个具体网页的详细内容，调用 FetchUrl 脚本并传入完整的 URL 地址。
        3. 将返回的信息整合为简洁、准确的回答，并注明信息来源。
        """;

    // ==================== 脚本：多引擎搜索 ====================
    [AgentSkillScript("Search")]
    [Description("使用多个搜索引擎搜索关键词，返回清洗后的网页文本摘要。")]
    public async Task<string> SearchAsync(
        [Description("搜索关键词或问题")] string query)
    {
        var results = new List<object>();

        foreach (var template in SearchTemplates)
        {
            try
            {
                var url = string.Format(template.Key, Uri.EscapeDataString(query));
                var html = await FetchHtmlAsync(url);
                if (string.IsNullOrWhiteSpace(html))
                    continue;

                var cleanText = CleanHtml(html);
                if (string.IsNullOrWhiteSpace(cleanText))
                    continue;

                results.Add(new
                {
                    Source = template.Value,
                    Url = url,
                    Content = cleanText
                });
            }
            catch (Exception ex)
            {
                // 单个引擎失败不影响整体结果
                Console.WriteLine($"[WebSearchSkill] 搜索 {template.Value} 失败: {ex.Message}");
            }
        }

        return JsonSerializer.Serialize(new { Query = query, Results = results },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    // ==================== 脚本：抓取指定 URL ====================
    [AgentSkillScript("FetchUrl")]
    [Description("抓取指定网页 URL 的内容，返回清洗后的纯文本。")]
    public async Task<string> FetchUrlAsync(
        [Description("完整的网页 URL，如 https://example.com")] string url)
    {
        try
        {
            var html = await FetchHtmlAsync(url);
            if (string.IsNullOrWhiteSpace(html))
                return "无法获取网页内容，请检查 URL 是否正确。";

            var cleanText = CleanHtml(html);
            return cleanText ?? "网页内容为空或无法解析。";
        }
        catch (Exception ex)
        {
            return $"获取网页失败：{ex.Message}";
        }
    }

    // ==================== 私有辅助 ====================

    /// <summary>
    /// 发起 HTTP 请求并返回网页原始 HTML 字符串（自动处理编码）。
    /// </summary>
    private static async Task<string?> FetchHtmlAsync(string url)
    {
        var handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true
        };

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Clear();
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        http.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        var response = await http.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        Encoding encoding = GetEncodingFromResponse(response) ?? DetectEncoding(bytes);
        return encoding.GetString(bytes);
    }

    /// <summary>
    /// 从响应头中尝试获取编码。
    /// </summary>
    private static Encoding? GetEncodingFromResponse(HttpResponseMessage response)
    {
        string? charset = response.Content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
            return null;

        try
        {
            return Encoding.GetEncoding(charset.Replace("\"", ""));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 简单启发式检测中文网页编码（UTF-8 或 GBK）。
    /// </summary>
    private static Encoding DetectEncoding(byte[] bytes)
    {
        string utf8Str = Encoding.UTF8.GetString(bytes);
        // 如果 UTF-8 解码后出现常见乱码字符，则判断为 GBK
        if (utf8Str.Contains('�') || utf8Str.Contains("锟斤拷"))
        {
            try { return Encoding.GetEncoding("GBK"); }
            catch { /* fallback */ }
        }
        return Encoding.UTF8;
    }

    /// <summary>
    /// 清洗 HTML：移除脚本、样式、注释、多余标签属性，压缩空白。
    /// </summary>
    private static string? CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        // 移除 script, style, 注释, head
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, @"<head[^>]*>[\s\S]*?</head>", "", RegexOptions.IgnoreCase);

        // 移除标签属性（保留标签名）
        html = Regex.Replace(html, @"<(\w+)(?:\s+[^>]*)?>", "<$1>");

        // 移除空的标签对（例如 <div></div>）
        html = Regex.Replace(html, @"<(\w+)(?:\s+[^>]*)?>\s*</\1>", "");

        // 统一换行
        html = html.Replace("\r\n", "\n").Replace("\r", "\n");

        // 压缩空白（合并多个空格和制表符，保留单个空格）
        html = Regex.Replace(html, @"[ \t]+", " ");

        // 压缩连续换行
        html = Regex.Replace(html, @"\n{3,}", "\n\n");

        // 移除首尾空白
        html = html.Trim();

        return string.IsNullOrWhiteSpace(html) ? null : html;
    }
}