using Microsoft.Extensions.AI;

public record AIAgentModel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public ChatOptions ChatOptions { get; set; } = new ChatOptions();
    public List<SkillMethodEntry> SelectedSkills { get; set; } = new();
    public List<McpModel> SelectedMcpModels { get; set; } = new();
}

public record SkillMethodEntry
{
    /// <summary>
    /// 插件名称 (例如: work-order-query)
    /// </summary>
    public string SkillName { get; set; }
}

public record McpModel
{
    public McpTypeEnum McpType { get; set; } = McpTypeEnum.Http;
    public string Name { get; set; }
    public string Description { get; set; }
    public string Command { get; set; }
    public string Url { get; set; }
    public List<string> AllowedTools { get; set; } = new();
    public string[] Arguments { get; set; }
}

public enum McpTypeEnum
{
    Http,
    Stdio
}



/// <summary>
/// 流式回调事件类型
/// </summary>
public enum StreamingEventType
{
    Thinking,       // 思考中
    TextChunk,      // 普通文本片段
    ToolCallStart,  // 工具调用开始
    ToolCallEnd,    // 工具调用结束
    EchartsStart,   // Echarts 渲染开始
    EchartsEnd      // Echarts 渲染结束

}

/// <summary>
/// 流式事件参数
/// </summary>
public class StreamingEventArgs
{
    public StreamingEventType EventType { get; init; }
    public string? Content { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
    public string CallId { get; init; }
}