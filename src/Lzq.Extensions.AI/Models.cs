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
}