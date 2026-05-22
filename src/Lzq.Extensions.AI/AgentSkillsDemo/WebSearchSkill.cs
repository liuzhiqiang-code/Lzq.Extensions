using Lzq.Extensions.AI.AgentSkills;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lzq.Extensions.AI.AgentSkillsDemo;

/// <summary>
/// 网络搜索技能 —— 提供高并发搜索引擎查询和网页内容防爆炸抓取能力。
/// </summary>
[GeneralSkill(SkillCategory.Network)]
public class WebSearchSkill : LzqAgentSkillBase<WebSearchSkill>
{
    // 搜索引擎模板
    private static readonly Dictionary<string, string> SearchTemplates = new()
    {
        { "https://cn.bing.com/search?q={0}", "必应搜索" },
        { "https://www.baidu.com/s?wd={0}&ie=utf-8&rn=8", "百度搜索" },
        { "https://kaifa.baidu.com/searchPage?wd={0}&ie=utf-8&rn=8", "百度开发搜索" },
        { "https://www.sogou.com/web?query={0}", "搜狗搜索" },
        { "https://www.so.com/s?q={0}", "360搜索" },
    };

    // ==================== 技能元数据 ====================
    protected override string SkillName => "web-search";
    protected override string SkillDescription => "提供互联网多引擎高并发检索和任意网页内容深度清洗抓取能力。当用户需要实时新闻、技术资料、查阅在线 URL 时使用。";

    // ==================== 核心指令 ====================
    protected override string Instructions => """
        你是一个拥有敏锐洞察力的网络搜索助手。当用户需要查找互联网上的实时信息时，请按以下指引操作：
        1. 调用 Search 脚本，传入高度提炼的关键词（不要传一整句话，提取核心词），脚本将并发调度多个引擎快速检索。
        2. 若搜索出来的摘要信息不足以回答问题，且发现了极具价值的官方文档/技术博客链接，调用 FetchUrl 脚本深入抓取该网页。
        3. 整合回答时，必须基于返回的事实，并在文末通过 [1](URL) 格式显式注明信息来源。
        """;

    // ==================== 脚本：高并发多引擎搜索 ====================
    [AgentSkillScript("Search")]
    [Description("【并发提速版】使用多个搜索引擎检索关键词，返回经过防噪、截断清洗后的高质量文本，拒绝 Token 溢出。")]
    public async Task<string> SearchAsync(
        [Description("搜索关键词或精炼问题，示例：'.net 10 preview features'")] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return JsonSerializer.Serialize(new { Query = query, Results = Array.Empty<object>(), Message = "关键词不能为空" });
        }

        // 1. 将串行循环升级为多任务并行，榨干网络 I/O 吞吐
        var tasks = SearchTemplates.Select(async template =>
        {
            try
            {
                var url = string.Format(template.Key, Uri.EscapeDataString(query));
                var html = await FetchHtmlAsync(url).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(html)) return null;

                var cleanText = CleanHtml(html);
                if (string.IsNullOrWhiteSpace(cleanText)) return null;

                // 【防噪防御一】：搜索引擎主页垃圾文字太多，单个引擎的摘要强制截断前 1500 字，足够大模型抽取快照
                if (cleanText.Length > 1500)
                {
                    cleanText = cleanText.Substring(0, 1500) + "... [内容因防 Token 爆仓已截断]";
                }

                return new
                {
                    Source = template.Value,
                    Url = url,
                    Content = cleanText
                };
            }
            catch (Exception ex)
            {
                // 单个引擎故障不影响全局大盘
                Console.WriteLine($"[WebSearchSkill] 并发搜索 {template.Value} 失败: {ex.Message}");
                return null;
            }
        });

        // 2. 并发等待所有引擎冲线
        var rawResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        var filteredResults = rawResults.Where(r => r != null).ToList();

        return JsonSerializer.Serialize(new
        {
            Query = query,
            EngineCount = filteredResults.Count,
            Results = filteredResults
        }, new JsonSerializerOptions
        {
            WriteIndented = false, // 关掉换行缩进，极致压榨传输体积，能省下不少 Token
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    // ==================== 脚本：抓取指定 URL ====================
    [AgentSkillScript("FetchUrl")]
    [Description("深入抓取指定网页 URL 的核心正文，返回经过高度降噪清洗后的纯文本（上限 8000 字）。")]
    public async Task<string> FetchUrlAsync(
        [Description("完整的网页规范 URL，如 https://learn.microsoft.com/dotnet")] string url)
    {
        try
        {
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "非法的 URL 格式，必须以 http:// 或 https:// 开头。";
            }

            var html = await FetchHtmlAsync(url).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
                return "无法获取网页内容，服务器返回内容为空。";

            var cleanText = CleanHtml(html);
            if (string.IsNullOrWhiteSpace(cleanText))
                return "网页解析失败，未能从中提取到有效文本（可能全为图片或被安全防爬拦截）。";

            // 【防噪防御二】：防止用户传个长篇小说网址，导致单次对话把上下文挤爆，设定硬上限 8000 字
            if (cleanText.Length > 8000)
            {
                cleanText = cleanText.Substring(0, 8000) + "\n\n[警告：由于单页文本量过大，系统已自动拦截执行硬截断，以保护大模型上下文。]";
            }

            return cleanText;
        }
        catch (Exception ex)
        {
            return $"抓取网页由于网络或目标防爬原因失败：{ex.Message}";
        }
    }

    // ==================== 私有底层核心 ====================

    private static async Task<string?> FetchHtmlAsync(string url)
    {
        var handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };

        // 注册 GBK 编码提供程序，防止在国内部分老旧 GBK 网页上抛出 NotSupportedException
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) }; // 搜索引擎时效性高，超时从 30s 缩短到 15s
        http.DefaultRequestHeaders.Clear();
        http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        http.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        var response = await http.GetAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        Encoding encoding = GetEncodingFromResponse(response) ?? DetectEncoding(bytes);
        return encoding.GetString(bytes);
    }

    private static Encoding? GetEncodingFromResponse(HttpResponseMessage response)
    {
        string? charset = response.Content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset)) return null;

        try { return Encoding.GetEncoding(charset.Replace("\"", "").Trim()); }
        catch { return null; }
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        string utf8Str = Encoding.UTF8.GetString(bytes);
        // 如果 UTF-8 解码后出现高频乱码字符，判定为国内高频的 GBK/GB2312
        if (utf8Str.Contains('\uFFFD') || utf8Str.Contains("锟斤拷"))
        {
            try { return Encoding.GetEncoding("GBK"); }
            catch { /* fallback */ }
        }
        return Encoding.UTF8;
    }

    /// <summary>
    /// 增强型网页去噪清洗引擎
    /// </summary>
    private static string? CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // 1. 拦截剥离无用代码大块区
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<noscript[^>]*>[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, @"<head[^>]*>[\s\S]*?</head>", "", RegexOptions.IgnoreCase);

        // 2. 【新增搜索引擎特定噪声清除】
        // 过滤如百度、360 的热搜榜、侧边栏、隐私底栏文本广告干扰项
        html = Regex.Replace(html, @"据百度网络服务使用协议[\s\S]*", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"相关搜索[\s\S]*", "", RegexOptions.IgnoreCase);

        // 3. 剥离所有 HTML 标签，仅保留纯文本
        html = Regex.Replace(html, @"<[^>]+>", " ");

        // 4. HTML 实体转义解码（如 &middot; -> · , &amp; -> &）
        html = WebUtility.HtmlDecode(html);

        // 5. 排版优化：合并并清理无意义的留白和乱换行
        html = html.Replace("\r\n", "\n").Replace("\r", "\n");
        html = Regex.Replace(html, @"[ \t]+", " "); // 压缩同行空格
        html = Regex.Replace(html, @"\n{2,}", "\n"); // 连续多行换行压缩为单行换行
        html = html.Trim();

        return string.IsNullOrWhiteSpace(html) ? null : html;
    }
}