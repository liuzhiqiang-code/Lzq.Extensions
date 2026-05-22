using System.Text.Json.Serialization;

namespace Lzq.Core.Models;

/// <summary>
/// Vben Admin Echarts 统一渲染引擎配置实体
/// </summary>
public class EchartsOption
{
    [JsonPropertyName("title")]
    public ChartTitle? Title { get; set; }

    [JsonPropertyName("tooltip")]
    public ChartTooltip? Tooltip { get; set; }

    [JsonPropertyName("legend")]
    public ChartLegend? Legend { get; set; }

    [JsonPropertyName("grid")]
    public ChartGrid? Grid { get; set; }

    /// <summary>
    /// 支持单轴对象 ChartAxis 或多轴列表 List&lt;ChartAxis&gt;
    /// </summary>
    [JsonPropertyName("xAxis")]
    public object? XAxis { get; set; }

    /// <summary>
    /// 支持单轴对象 ChartAxis 或多轴列表 List&lt;ChartAxis&gt;
    /// </summary>
    [JsonPropertyName("yAxis")]
    public object? YAxis { get; set; }

    [JsonPropertyName("radar")]
    public ChartRadar? Radar { get; set; }

    [JsonPropertyName("series")]
    public List<ChartSeries> Series { get; set; } = new();

    /// <summary>
    /// 数据区域缩放组件。用于大数据量时底部的拖拽缩放滑块
    /// </summary>
    [JsonPropertyName("dataZoom")]
    public List<ChartDataZoom>? DataZoom { get; set; }

    /// <summary>
    /// 根节点万能兜底口袋：大模型生成的不在上述强类型中的配置项（如 color, toolbox 等）会自动收集到这里并平铺序列化给前端
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtraOptions { get; set; }
}

#region 1. 基础公共配置项

public class ChartTitle
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class ChartTooltip
{
    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; } // 'axis' | 'item'

    [JsonPropertyName("axisPointer")]
    public ChartAxisPointer? AxisPointer { get; set; }
}

public class ChartAxisPointer
{
    [JsonPropertyName("lineStyle")]
    public ChartLineStyle? LineStyle { get; set; }
}

public class ChartLegend
{
    [JsonPropertyName("bottom")]
    public object? Bottom { get; set; }

    /// <summary>
    /// 水平对齐位置，对应环形图的 left: 'center'
    /// </summary>
    [JsonPropertyName("left")]
    public object? Left { get; set; }

    [JsonPropertyName("data")]
    public List<string>? Data { get; set; }

    /// <summary>
    /// 图例类型。可选值: 'plain' | 'scroll' (数量多时自动带翻页箭头)
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class ChartGrid
{
    [JsonPropertyName("left")]
    public string? Left { get; set; }
    [JsonPropertyName("right")]
    public string? Right { get; set; }
    [JsonPropertyName("bottom")]
    public object? Bottom { get; set; }
    [JsonPropertyName("top")]
    public string? Top { get; set; }
    [JsonPropertyName("containLabel")]
    public bool? ContainLabel { get; set; }
}

public class ChartAxis
{
    [JsonPropertyName("type")]
    public string? Type { get; set; } // 'category' | 'value' | 'time' | 'log'

    [JsonPropertyName("data")]
    public List<object>? Data { get; set; } // 轴数据可能包含纯数字或对象，用 object 承载最稳妥

    [JsonPropertyName("boundaryGap")]
    public bool? BoundaryGap { get; set; }

    [JsonPropertyName("inverse")]
    public bool? Inverse { get; set; }

    [JsonPropertyName("max")]
    public object? Max { get; set; } // 有时大模型会下发 'dataMax' 字符串，改用 object 兼容

    [JsonPropertyName("splitNumber")]
    public int? SplitNumber { get; set; }

    [JsonPropertyName("axisTick")]
    public Dictionary<string, object>? AxisTick { get; set; }

    [JsonPropertyName("splitLine")]
    public ChartSplitLine? SplitLine { get; set; }

    [JsonPropertyName("splitArea")]
    public Dictionary<string, object>? SplitArea { get; set; }

    /// <summary>
    /// 轴节点万能兜底（常用于大模型自主调整 axisLabel.rotate 等偏门控制）
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtraOptions { get; set; }
}

#endregion

#region 2. 坐标系与图表样式配置

public class ChartRadar
{
    [JsonPropertyName("radius")]
    public object? Radius { get; set; }

    [JsonPropertyName("splitNumber")]
    public int? SplitNumber { get; set; }

    [JsonPropertyName("indicator")]
    public List<RadarIndicator>? Indicator { get; set; }
}

public class RadarIndicator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("max")]
    public decimal? Max { get; set; }
}

public class ChartSeries
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "line";

    [JsonPropertyName("smooth")]
    public bool? Smooth { get; set; }

    [JsonPropertyName("symbolSize")]
    public int? SymbolSize { get; set; }

    [JsonPropertyName("barMaxWidth")]
    public int? BarMaxWidth { get; set; }

    [JsonPropertyName("radius")]
    public object? Radius { get; set; } // 支持 '80%' 或 ['40%', '65%']

    /// <summary>
    /// 修复：支持混入数字和百分比，如 [400, '50%'] 或 ['50%', '50%']
    /// </summary>
    [JsonPropertyName("center")]
    public object[]? Center { get; set; }

    [JsonPropertyName("roseType")]
    public string? RoseType { get; set; }

    [JsonPropertyName("color")]
    public string[]? Color { get; set; }

    /// <summary>
    /// 是否防止标签重叠，环形图标准配置
    /// </summary>
    [JsonPropertyName("avoidLabelOverlap")]
    public bool? AvoidLabelOverlap { get; set; }

    /// <summary>
    /// 标签配置，用于控制是否在环形图中心显示/隐藏
    /// </summary>
    [JsonPropertyName("label")]
    public ChartLabelConfig? Label { get; set; }

    /// <summary>
    /// 视觉引导线配置
    /// </summary>
    [JsonPropertyName("labelLine")]
    public Dictionary<string, object>? LabelLine { get; set; }

    /// <summary>
    /// 高亮扇区时的联动样式设定
    /// </summary>
    [JsonPropertyName("emphasis")]
    public ChartEmphasis? Emphasis { get; set; }

    [JsonPropertyName("data")]
    public List<object> Data { get; set; } = new();

    [JsonPropertyName("itemStyle")]
    public SeriesItemStyle? ItemStyle { get; set; }

    [JsonPropertyName("areaStyle")]
    public ChartAreaStyle? AreaStyle { get; set; }

    [JsonPropertyName("animationType")]
    public string? AnimationType { get; set; }

    [JsonPropertyName("animationEasing")]
    public string? AnimationEasing { get; set; }

    /// <summary>
    /// 不同系列柱间距离。可设为百分比（如 "30%"）或固定数字
    /// </summary>
    [JsonPropertyName("barGap")]
    public string? BarGap { get; set; }

    /// <summary>
    /// 数据堆叠，同名的一组系列会自动堆叠展现
    /// </summary>
    [JsonPropertyName("stack")]
    public string? Stack { get; set; }

    /// <summary>
    /// 图表标注点（如圈出最大值、最小值）
    /// </summary>
    [JsonPropertyName("markPoint")]
    public ChartMark? MarkPoint { get; set; }

    /// <summary>
    /// 图表标注线（如自动绘制平均值横线）
    /// </summary>
    [JsonPropertyName("markLine")]
    public ChartMark? MarkLine { get; set; }

    /// <summary>
    /// 系列万能兜底口袋（大模型生成特定图表如 funnel 漏斗图、gauge 仪表盘的特有属性时自动吞吐）
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtraOptions { get; set; }
}

#endregion

#region 3. 标签与高亮联动专用子实体

public class ChartLabelConfig
{
    [JsonPropertyName("show")]
    public bool Show { get; set; }

    /// <summary>
    /// 标签位置，可选: 'outside' | 'inside' | 'center'
    /// </summary>
    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("fontSize")]
    public object? FontSize { get; set; } // 可以是数字 12 或字符串 '12'

    [JsonPropertyName("fontWeight")]
    public string? FontWeight { get; set; }
}

public class ChartEmphasis
{
    [JsonPropertyName("label")]
    public ChartLabelConfig? Label { get; set; }
}

public class SeriesItemStyle
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("borderRadius")]
    public object? BorderRadius { get; set; }

    [JsonPropertyName("borderWidth")]
    public int? BorderWidth { get; set; }
}

public class ChartAreaStyle
{
    [JsonPropertyName("opacity")]
    public double? Opacity { get; set; }
    [JsonPropertyName("shadowBlur")]
    public int? ShadowBlur { get; set; }
    [JsonPropertyName("shadowColor")]
    public string? ShadowColor { get; set; }
    [JsonPropertyName("shadowOffsetX")]
    public int? ShadowOffsetX { get; set; }
    [JsonPropertyName("shadowOffsetY")]
    public int? ShadowOffsetY { get; set; }
}

public class ChartLineStyle
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }
    [JsonPropertyName("width")]
    public int? Width { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class ChartSplitLine
{
    [JsonPropertyName("show")]
    public bool Show { get; set; }

    [JsonPropertyName("lineStyle")]
    public ChartLineStyle? LineStyle { get; set; }
}

#endregion

#region 4. 辅助子实体

public class ChartDataZoom
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "slider";

    [JsonPropertyName("show")]
    public bool Show { get; set; } = true;

    [JsonPropertyName("start")]
    public int? Start { get; set; } = 0;

    [JsonPropertyName("end")]
    public int? End { get; set; } = 100;
}

public class ChartMark
{
    [JsonPropertyName("data")]
    public List<Dictionary<string, string>> Data { get; set; } = new();
}

#endregion