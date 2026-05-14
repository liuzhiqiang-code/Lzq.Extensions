using System.Text.Json.Serialization;

namespace Lzq.Extensions.AI.AgentSkills;

public interface ISkillManager
{
    IEnumerable<SkillInfo> GetSkills();
    Task<object?> ExecuteAsync(string skillName, string toolName, Dictionary<string, object>? arguments);

    /// <summary>
    /// 上传技能程序集文件（DLL）
    /// </summary>
    Task UploadPluginAsync(string fileName, Stream fileStream);

    /// <summary>
    /// 上传外部技能压缩包（ZIP）
    /// </summary>
    Task UploadExternalSkillZipAsync(string fileName, Stream fileStream);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SkillType
{
    /// <summary>内部类技能（Dll 插件）</summary>
    Internal,
    /// <summary>外部文件技能（SKILL.md 目录）</summary>
    External
}

public record SkillInfo(string SkillName, string SkillDescription, SkillType Type, IReadOnlyList<ToolInfo> Tools);
public record ToolInfo(string ToolName, string Description, IReadOnlyList<ParameterInfo> Parameters);
public record ParameterInfo(string Name, string ParameterType, string Description);