using Microsoft.Agents.AI;
using System.Diagnostics.CodeAnalysis;

namespace Lzq.Extensions.AI.AgentSkills;

public abstract class LzqAgentSkillBase<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TSelf>
    : AgentClassSkill<TSelf> where TSelf : LzqAgentSkillBase<TSelf>
{
    protected abstract string SkillName { get; }
    protected abstract string SkillDescription { get; }
    public sealed override AgentSkillFrontmatter Frontmatter => new(
        name: SkillName.ToLowerInvariant(),
        description: SkillDescription
    );
}