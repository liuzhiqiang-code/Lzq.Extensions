using Microsoft.Extensions.AI;

public record AIAgentModel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public ChatOptions ChatOptions { get; set; } = new ChatOptions();

    public List<SkillMethodEntry> SelectedSkills { get; set; } = new();
}

public record SkillMethodEntry
{
    /// <summary>
    /// 插件名称 (例如: work-order-query)
    /// </summary>
    public string SkillName { get; set; }

    /// <summary>
    /// 该插件下需要挂载的方法名列表
    /// 如果为空，则默认挂载该 Skill 下所有标记了 [AgentSkillScript] 的方法
    /// </summary>
    public List<string> ToolNames { get; set; } = new();
}