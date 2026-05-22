using Lzq.Core.Models;
using Lzq.Extensions.AI.AgentSkills;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lzq.Extensions.AI.AgentSkillsDemo;

[GeneralSkill(SkillCategory.Visual)]
public class GenericDataAnalysisSkill : LzqAgentSkillBase<GenericDataAnalysisSkill>
{
    protected override string SkillName => "generic-data-analyzer";
    protected override string SkillDescription => "通用数据分析中枢。通过接收自然语言提炼的参数，结合外部工具获取的数据，动态审计、处理并生成标准企业大盘图表。";

    protected override string Instructions => """
        你是一个资深的数据分析专家和低代码架构师。当用户需要分析、统计或可视化某些业务数据时，请严格按以下流程处理：

        1. 从用户话术中提炼核心诉求，并优先通过数据检索工具或知识库脚本获取原始 JSON 数据（如一组 name/value 对）。
        2. 调用本技能的 GenerateChart 脚本，将提取的配置意图和原始数据通过参数传入。
           - 如需对比两组数据（例如计划 vs 实际），可在每个数据项中添加 `value2` 字段。
        3. 如果脚本返回了『参数缺失』或『数据无效』的提示，请用温和的语气告知用户并请求补充信息，然后**立即停止**。
        4. 如果脚本成功返回了 JSON 字符串，你必须**直接立刻结束回答，不得添加任何前言、总结、解释、表情符号或额外文字。图表将自动在前端渲染。**
        """;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [AgentSkillScript("GenerateChart")]
    [Description("根据模型提取的图表意图和外部工具获取的原始数据，进行条件审计并生成前端统一渲染引擎所需的标准 EchartsOption 结构。")]
    public Task<string> GenerateChartAsync(
        [Description("自然语言提炼的图表类型，必填。可选值: 'line' | 'bar' | 'pie' | 'radar'")] string chartType,
        [Description("外部工具/API 获取的原始 JSON 数组数据，必填。格式示例: [{\"name\":\"Wish\",\"value\":150}]")] string rawJsonData,
        [Description("图表主标题，选填。")] string? title = null,
        [Description("多系列堆叠标识，选填。")] bool? isStacked = null,
        [Description("大数据量滚动滑块，选填。")] bool? enableZoom = null)
    {
        // 1. 审计图表类型
        if (string.IsNullOrWhiteSpace(chartType))
            return Error("chartType", "未能提炼出明确的图表展现形式（如折线、柱状、饼图）。");

        var normalizedType = chartType.ToLower().Trim();
        if (!new[] { "line", "bar", "pie", "radar" }.Contains(normalizedType))
            return Error("chartType", $"暂不支持渲染 '{chartType}' 类型的图表，目前仅支持 line/bar/pie/radar。");

        // 2. 审计原始数据
        if (string.IsNullOrWhiteSpace(rawJsonData))
            return Error("rawJsonData", "未能获取到有效的业务底层数据，请先通过数据查询工具或知识库检索相关指标。");

        // 处理可能发生的双重 JSON 序列化
        string dataJson = UnwrapDoubleSerialization(rawJsonData);

        JsonDocument jsonDoc;
        try
        {
            jsonDoc = JsonDocument.Parse(dataJson);
        }
        catch (JsonException)
        {
            return Error("rawJsonData", "底层数据解析失败，传入的非标准 JSON 字符串，请检查数据源输出。");
        }

        if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
            return Error("rawJsonData", "数据格式审计未通过：必须为 JSON 对象数组。");

        // 3. 提取并校验数据
        var categories = new List<string>();
        var values = new List<object>();
        var values2 = new List<object>();      // 第二系列数据（可选）
        bool hasSecondSeries = false;

        foreach (var element in jsonDoc.RootElement.EnumerateArray())
        {
            // 弹性键值提取名称
            string name = element.TryGetProperty("name", out var n) ? n.ToString() :
                          element.TryGetProperty("code", out var c) ? c.ToString() :
                          element.TryGetProperty("label", out var l) ? l.ToString() : "未分类";

            // 主值
            object val = element.TryGetProperty("value", out var v) ? (object)v.GetDouble() :
                         element.TryGetProperty("amount", out var a) ? (object)a.GetDouble() :
                         element.TryGetProperty("count", out var cnt) ? (object)cnt.GetDouble() : 0.0;

            categories.Add(name);
            values.Add(val);

            // 第二系列值（可选）
            object? val2 = null;
            if (element.TryGetProperty("value2", out var v2))
            {
                hasSecondSeries = true;
                val2 = v2.GetDouble();
            }
            else if (element.TryGetProperty("amount2", out var a2))
            {
                hasSecondSeries = true;
                val2 = a2.GetDouble();
            }
            values2.Add(val2 ?? 0.0);
        }

        // 数据质量检查：只有全 0 且全“未分类”才报错
        if (categories.All(c => c == "未分类") && values.All(v => (double)v == 0.0))
            return Error("rawJsonData", "所有数值均为0且缺少有效名称，请提供有意义的数据。");

        // 4. 构建 ECharts 配置
        var finalTitle = !string.IsNullOrWhiteSpace(title) ? title : $"{GetChartTypeName(normalizedType)}综合业务智能动态分析";
        bool finalStacked = isStacked ?? false;
        int dataCount = jsonDoc.RootElement.GetArrayLength();
        bool finalEnableZoom = enableZoom ?? (dataCount > 15);

        var option = new EchartsOption
        {
            Title = new ChartTitle { Text = finalTitle },
            Tooltip = new ChartTooltip
            {
                Trigger = normalizedType == "pie" ? "item" : "axis",
                AxisPointer = normalizedType == "pie" ? null : new ChartAxisPointer { LineStyle = new ChartLineStyle { Type = "dashed" } }
            },
            Legend = new ChartLegend { Bottom = "0%", Type = "scroll" },
            Grid = new ChartGrid { Left = "3%", Right = "4%", Top = "60", Bottom = "10%", ContainLabel = true }
        };

        if (normalizedType == "pie")
        {
            // 饼图不支持多系列，忽略 value2
            option.Grid = null;
            option.XAxis = null;
            option.YAxis = null;
            option.Series.Add(new ChartSeries
            {
                Type = "pie",
                Radius = new[] { "40%", "70%" },
                AvoidLabelOverlap = true,
                Label = new ChartLabelConfig { Show = false, Position = "center" },
                Emphasis = new ChartEmphasis { Label = new ChartLabelConfig { Show = true, FontSize = "18", FontWeight = "bold" } },
                Data = categories.Zip(values, (c, v) => new { value = v, name = c }).Cast<object>().ToList()
            });
        }
        else
        {
            option.XAxis = new ChartAxis { Type = "category", Data = categories.Cast<object>().ToList(), BoundaryGap = (normalizedType == "bar") };
            option.YAxis = new ChartAxis { Type = "value" };

            // 主系列
            var mainSeries = new ChartSeries
            {
                Type = normalizedType,
                Stack = finalStacked ? "total" : null,
                BarMaxWidth = normalizedType == "bar" ? 30 : null,
                Smooth = normalizedType == "line" ? true : null,
                Data = values,
                MarkLine = new ChartMark
                {
                    Data = new List<Dictionary<string, string>> { new() { { "type", "average" }, { "name", "均值线" } } }
                }
            };
            option.Series.Add(mainSeries);

            // 如果存在第二系列，添加第二个系列（不带 MarkLine 避免重复均值线）
            if (hasSecondSeries)
            {
                var secondSeries = new ChartSeries
                {
                    Name = "系列2",
                    Type = normalizedType,
                    Stack = finalStacked ? "total" : null,
                    BarMaxWidth = normalizedType == "bar" ? 30 : null,
                    Smooth = normalizedType == "line" ? true : null,
                    Data = values2
                };
                option.Series.Add(secondSeries);
            }
        }

        if (finalEnableZoom)
        {
            option.DataZoom = new List<ChartDataZoom> { new() { Type = "slider", Show = true, Start = 0, End = 100 } };
        }

        return Task.FromResult(JsonSerializer.Serialize(option, _jsonOptions));
    }

    #region 辅助方法
    private static Task<string> Error(string field, string message)
    {
        var response = new
        {
            success = false,
            errorCode = "CONDITION_NOT_MET",
            missingField = field,
            message = $"[执行被拦截] 条件不足：{message}"
        };
        return Task.FromResult(JsonSerializer.Serialize(response, _jsonOptions));
    }

    private static string UnwrapDoubleSerialization(string raw)
    {
        if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length >= 2)
        {
            try
            {
                var inner = JsonSerializer.Deserialize<string>(raw);
                if (inner != null && (inner.StartsWith("[") || inner.StartsWith("{")))
                    return inner;
            }
            catch { }
        }
        return raw;
    }

    private static string GetChartTypeName(string type) => type switch
    {
        "line" => "趋势折线图",
        "bar" => "对比柱状图",
        "pie" => "构成比例图",
        "radar" => "多维雷达图",
        _ => "业务分析图"
    };
    #endregion
}