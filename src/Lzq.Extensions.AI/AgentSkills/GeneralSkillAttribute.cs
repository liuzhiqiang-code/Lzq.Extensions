namespace Lzq.Extensions.AI.AgentSkills;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneralSkillAttribute : Attribute
{
    public SkillCategory Category { get; }
    public bool AutoLoad { get; }

    public GeneralSkillAttribute(SkillCategory category = SkillCategory.Core, bool autoLoad = false)
    {
        Category = category;
        AutoLoad = autoLoad;
    }
}

/// <summary>
/// 技能大类枚举
/// </summary>
public enum SkillCategory
{
    Core,       // 基础/核心工具
    Visual,     // 视觉/图表展示类工具
    Network,    // 网络/搜索类工具
    Office      // 文档/办公自动化工具
}