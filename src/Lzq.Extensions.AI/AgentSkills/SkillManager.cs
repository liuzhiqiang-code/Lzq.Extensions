using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace Lzq.Extensions.AI.AgentSkills;

public class SkillManager : ISkillManager
{
    private readonly AgentSkillProvider _provider;
    private readonly ILogger<SkillManager> _logger;

    public SkillManager(AgentSkillProvider provider, ILogger<SkillManager> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    /// <summary>
    /// 列出所有类技能及其工具信息（用于查看技能库）
    /// </summary>
    public IEnumerable<SkillInfo> GetSkills()
    {
        // 1. 内部类技能
        foreach (var skill in _provider.GetSkills())
        {
            try
            {
                if (skill is null) continue;

                var type = skill.GetType();
                var tools = GetToolInfos(type);

                yield return new SkillInfo(
                    skill.Frontmatter.Name,
                    skill.Frontmatter.Description,
                    SkillType.Internal,
                    tools
                );
            }
            finally
            {
                (skill as IDisposable)?.Dispose();
            }
        }

        // 2. 外部文件技能
        foreach (var skillDir in _provider.GetExternalSkillDirectories())
        {
            var mdFile = Path.Combine(skillDir, "SKILL.md");
            if (!File.Exists(mdFile)) continue;

            var (name, description) = ParseSkillMetadata(mdFile);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var tools = GetExternalToolInfos(skillDir);
            yield return new SkillInfo(
                name,
                description ?? "无描述",
                SkillType.External,
                tools
            );
        }
    }

    // 解析外部技能的 name 和 description（复用你已有的 ParseSkillNameFromMarkdown 逻辑，扩展为获取 description）
    private static (string? name, string? description) ParseSkillMetadata(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            bool inFrontMatter = false;
            string? name = null, description = null;
            foreach (var line in lines)
            {
                if (line.Trim() == "---")
                {
                    if (!inFrontMatter) { inFrontMatter = true; continue; }
                    else break;
                }
                if (inFrontMatter && line.StartsWith("name:"))
                    name = line["name:".Length..].Trim();
                if (inFrontMatter && line.StartsWith("description:"))
                    description = line["description:".Length..].Trim();
            }
            return (name, description);
        }
        catch { return (null, null); }
    }

    // 获取外部技能的工具列表：扫描 scripts/ 目录下的所有文件
    private static IReadOnlyList<ToolInfo> GetExternalToolInfos(string skillDir)
    {
        var scriptsPath = Path.Combine(skillDir, "scripts");
        if (!Directory.Exists(scriptsPath))
            return Array.Empty<ToolInfo>();

        return Directory.GetFiles(scriptsPath)
            .Select(file => new ToolInfo(
                ToolName: Path.GetFileNameWithoutExtension(file),
                Description: "外部脚本（无详细描述）",
                Parameters: Array.Empty<ParameterInfo>()))
            .ToList();
    }

    /// <summary>
    /// 手动执行某个技能的指定工具（供调试/测试用）
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">参数键值对</param>
    /// <returns>执行结果，如果失败则返回错误信息</returns>
    public async Task<object?> ExecuteAsync(string skillName, string toolName, Dictionary<string, object?>? arguments)
    {
        // 获取技能实例
        var skill = _provider.GetClassSkillByName(skillName);
        if (skill is null)
        {
            _logger.LogWarning("未找到技能：{SkillName}", skillName);
            return null;
        }

        try
        {
            // 查找待调用的方法
            var method = FindToolMethod(skill, toolName);
            if (method is null)
            {
                _logger.LogWarning("技能 {SkillName} 中未找到工具 {ToolName}", skillName, toolName);
                return null;
            }

            // 构造参数数组
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                // 尝试从字典获取值
                if (arguments is not null && arguments.TryGetValue(p.Name!, out var val))
                {
                    try
                    {
                        args[i] = ConvertParameterValue(val, p.ParameterType);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "参数 {ParamName} 值转换失败", p.Name);
                        return $"参数 {p.Name} 转换错误：{ex.Message}";
                    }
                }
                else if (p.HasDefaultValue)
                {
                    args[i] = p.DefaultValue;
                }
                else
                {
                    _logger.LogWarning("缺少必需参数：{ParamName}", p.Name);
                    return $"缺少必需参数：{p.Name}";
                }
            }

            // 执行方法
            var result = method.Invoke(skill, args);
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                // 提取 Task<T> 的 Result 属性
                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    var resultProp = taskType.GetProperty("Result");
                    if (resultProp is not null)
                        return resultProp.GetValue(task);
                }
                // 非泛型 Task（async void 或 Task），返回 null
                return null;
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行技能 {SkillName}.{ToolName} 失败", skillName, toolName);
            return $"执行失败：{ex.InnerException?.Message ?? ex.Message}";
        }
        finally
        {
            (skill as IDisposable)?.Dispose();
        }
    }

    // 获取工具信息列表（从类型反射）
    private static IReadOnlyList<ToolInfo> GetToolInfos(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => new { Method = m, Attr = m.GetCustomAttribute<AgentSkillScriptAttribute>() })
            .Where(x => x.Attr is not null)
            .Select(x =>
            {
                var method = x.Method;
                var attr = x.Attr!;
                var desc = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "无描述";
                var parameters = method.GetParameters()
                    .Select(p => new ParameterInfo(
                        p.Name ?? "unknown",
                        GetFriendlyTypeName(p.ParameterType),
                        p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "无描述"
                    ))
                    .ToList();

                return new ToolInfo(
                    ToolName: attr.Name ?? method.Name,
                    Description: desc,
                    Parameters: parameters
                );
            })
            .ToList();
    }

    // 根据名称和技能实例查找方法
    private static MethodInfo? FindToolMethod(AgentSkill skill, string toolName)
    {
        return skill.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
            {
                var attr = m.GetCustomAttribute<AgentSkillScriptAttribute>();
                return attr is not null &&
                       (attr.Name ?? m.Name).Equals(toolName, StringComparison.OrdinalIgnoreCase);
            });
    }

    // 将对象转换为指定类型（更健壮的转换）
    private static object? ConvertParameterValue(object? value, Type targetType)
    {
        if (value is null) return null;

        // 如果目标类型是可空值类型，则取底层类型
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsInstanceOfType(value))
            return value;

        // 处理 JsonElement（如果是来自 JSON 反序列化的字典）
        if (value is JsonElement jsonElement)
        {
            return jsonElement.Deserialize(underlyingType);
        }

        // 最后的回退：使用 Convert.ChangeType
        return Convert.ChangeType(value.ToString(), underlyingType);
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            var genericName = type.Name.Split('`')[0];
            var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
            return $"{genericName}<{args}>";
        }
        return type.Name switch
        {
            "Int32" => "int",
            "Int64" => "long",
            "Double" => "double",
            "Single" => "float",
            "Boolean" => "bool",
            "String" => "string",
            _ => type.Name
        };
    }

    // ======================== 新增：上传程序集 ========================
    public async Task UploadPluginAsync(string fileName, Stream fileStream)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("文件名不能为空", nameof(fileName));
        if (fileStream == null || fileStream.Length == 0)
            throw new ArgumentException("文件流不能为空", nameof(fileStream));
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只允许上传 .dll 文件");

        var savePath = Path.Combine(_provider.PluginPath, fileName);

        using (var fs = new FileStream(savePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        _provider.TriggerPluginReload();
        _logger.LogInformation("技能程序集 {FileName} 已上传并触发热加载", fileName);
    }

    // ======================== 新增：上传外部技能压缩包 ========================
    public async Task UploadExternalSkillZipAsync(string fileName, Stream fileStream)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("文件名不能为空", nameof(fileName));
        if (fileStream == null || fileStream.Length == 0)
            throw new ArgumentException("文件流不能为空", nameof(fileStream));
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只允许上传 .zip 文件");

        var externalDir = _provider.ExternalSkillsPath;

        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                // 过滤非法路径和目录
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.Contains(".."))
                    continue;

                var destPath = Path.GetFullPath(Path.Combine(externalDir, entry.FullName));
                if (!destPath.StartsWith(externalDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsDirectory(entry))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }

        _provider.TriggerExternalSkillsRefresh();
        _logger.LogInformation("外部技能压缩包 {FileName} 已解压并触发热加载", fileName);
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\");
}