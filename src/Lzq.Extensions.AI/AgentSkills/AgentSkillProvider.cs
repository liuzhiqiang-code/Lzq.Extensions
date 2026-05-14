using Masa.BuildingBlocks.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Lzq.Extensions.AI.AgentSkills;

public class AgentSkillProvider : IDisposable
{
    public string PluginPath => _pluginPath;
    public string ExternalSkillsPath => _externalSkillsPath!;
    public void TriggerPluginReload() => ReloadSkills();
    public void TriggerExternalSkillsRefresh() => RefreshExternalSkillNames();

    private readonly IServiceProvider _serviceProvider;
    private readonly string _pluginPath;
    private readonly string? _externalSkillsPath;
    private readonly FileSystemWatcher _dllWatcher;
    private readonly FileSystemWatcher _externalMdWatcher;

    private readonly ConcurrentDictionary<string, Type> _skillTypes = new();
    private readonly ConcurrentDictionary<Type, bool> _isGeneralCache = new();
    private ConcurrentDictionary<string, byte> _externalSkillNamesCache = new();

    public AgentSkillProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _pluginPath = Path.Combine(AppContext.BaseDirectory, "AgentSkills");
        _externalSkillsPath = Path.Combine(AppContext.BaseDirectory, "ExternalSkills");

        if (!Directory.Exists(_pluginPath)) Directory.CreateDirectory(_pluginPath);
        if (!Directory.Exists(_externalSkillsPath)) Directory.CreateDirectory(_externalSkillsPath);

        ReloadSkills();
        RefreshExternalSkillNames();

        // DLL 监听
        _dllWatcher = new FileSystemWatcher(_pluginPath, "*.dll");
        _dllWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
        _dllWatcher.Changed += OnDllChanged;
        _dllWatcher.Created += OnDllChanged;
        _dllWatcher.Deleted += OnDllChanged;
        _dllWatcher.EnableRaisingEvents = true;

        // 外部 Skills .md 监听
        _externalMdWatcher = new FileSystemWatcher(_externalSkillsPath, "*.md");
        _externalMdWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        _externalMdWatcher.Changed += OnExternalMdChanged;
        _externalMdWatcher.Created += OnExternalMdChanged;
        _externalMdWatcher.Deleted += OnExternalMdChanged;
        _externalMdWatcher.IncludeSubdirectories = true;
        _externalMdWatcher.EnableRaisingEvents = true;
    }

    #region 防抖与热加载

    private CancellationTokenSource? _dllReloadCts;
    private CancellationTokenSource? _externalReloadCts;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private void OnDllChanged(object sender, FileSystemEventArgs e)
    {
        _dllReloadCts?.Cancel();
        _dllReloadCts = new CancellationTokenSource();
        var token = _dllReloadCts.Token;
        Task.Delay(300, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                ReloadSkills();
        }, token);
    }

    private void OnExternalMdChanged(object sender, FileSystemEventArgs e)
    {
        _externalReloadCts?.Cancel();
        _externalReloadCts = new CancellationTokenSource();
        var token = _externalReloadCts.Token;
        Task.Delay(300, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                RefreshExternalSkillNames();
        }, token);
    }

    private void ReloadSkills()
    {
        if (!_reloadLock.Wait(0)) return;
        try
        {
            Console.WriteLine("[LzqNet] 正在刷新插件列表...");
            _skillTypes.Clear();
            _isGeneralCache.Clear();

            var currentAssemblies = MasaApp.GetAssemblies();
            foreach (var assembly in currentAssemblies)
            {
                if (assembly.FullName!.StartsWith("System") || assembly.FullName.StartsWith("Microsoft") || assembly.FullName.StartsWith("Masa"))
                    continue;
                ScanAssemblyTypes(assembly);
            }

            if (Directory.Exists(_pluginPath))
            {
                foreach (var file in Directory.GetFiles(_pluginPath, "*.dll"))
                {
                    try
                    {
                        var assemblyName = Path.GetFileNameWithoutExtension(file);
                        if (currentAssemblies.Any(a => a.GetName().Name == assemblyName))
                            continue;

                        byte[] assemblyBytes = File.ReadAllBytes(file);
                        var pdbPath = Path.ChangeExtension(file, ".pdb");
                        Assembly assembly;
                        if (File.Exists(pdbPath))
                        {
                            try
                            {
                                byte[] pdbBytes = File.ReadAllBytes(pdbPath);
                                assembly = Assembly.Load(assemblyBytes, pdbBytes);
                            }
                            catch
                            {
                                assembly = Assembly.Load(assemblyBytes);
                            }
                        }
                        else
                        {
                            assembly = Assembly.Load(assemblyBytes);
                        }
                        ScanAssemblyTypes(assembly);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LzqNet] 加载外部插件 {file} 失败: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    #endregion

    #region 类型扫描

    private void ScanAssemblyTypes(Assembly assembly)
    {
        try
        {
            var types = assembly.GetTypes().Where(t =>
                t is { IsClass: true, IsAbstract: false } &&
                IsSubclassOfRawGeneric(typeof(LzqAgentSkillBase<>), t));

            foreach (var type in types)
            {
                _skillTypes.TryAdd(type.FullName!, type);

                if (!_isGeneralCache.ContainsKey(type))
                {
                    try
                    {
                        _isGeneralCache[type] = type.GetCustomAttribute<GeneralSkillAttribute>() != null;
                    }
                    catch
                    {
                        _isGeneralCache[type] = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var msg = $"[LzqNet] 扫描程序集失败: {assembly.FullName}, 错误: {ex.Message}";
            Console.WriteLine(msg);
#if DEBUG
            throw new InvalidOperationException(msg, ex);
#endif
        }
    }

    private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (generic == cur) return true;
            toCheck = toCheck.BaseType;
        }
        return false;
    }

    #endregion

    #region 构建 AgentSkillsProvider

    /// <summary>
    /// 根据所选技能构建 <see cref="AgentSkillsProvider"/>。
    /// 自动注入通用技能，并按需加载指定的类技能和外部文件技能。
    /// </summary>
    public AgentSkillsProvider BuildAgentSkillsProviderBySelectedSkills(List<SkillMethodEntry> SelectedSkills)
    {
        var builder = new AgentSkillsProviderBuilder();

        // 1. 通用技能
        foreach (var generalSkill in GetGeneralSkills())
        {
            builder.UseSkill(generalSkill);
        }

        // 2. 指定的类技能
        if (SelectedSkills?.Any() == true)
        {
            foreach (var entry in SelectedSkills)
            {
                var skill = GetClassSkillByName(entry.SkillName);
                if (skill is not null)
                {
                    builder.UseSkill(skill);
                }
            }
        }

        // 3. 外部文件技能（全局加载 + 禁用脚本执行 + 精确筛选）
        if (!string.IsNullOrEmpty(_externalSkillsPath) && Directory.Exists(_externalSkillsPath))
        {
            builder
                .UseFileSkill(_externalSkillsPath, scriptRunner: NoOpScriptRunner)
                .UseFilter(skill =>
                {
                    if (SelectedSkills == null || SelectedSkills.Count == 0) return true;
                    return SelectedSkills.Any(s =>
                        s.SkillName.Equals(skill.Frontmatter.Name, StringComparison.OrdinalIgnoreCase));
                });
        }

        return builder
            .UseOptions(options => options.DisableCaching = true)
            .Build();
    }

    /// <summary>
    /// 无操作脚本运行器，符合 <see cref="AgentFileSkillScriptRunner"/> 签名。
    /// 禁用外部技能脚本执行，仅返回提示信息。
    /// </summary>
    private static Task<object?> NoOpScriptRunner(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>($"脚本已禁用: {script.Name}");
    }

    #endregion

    #region 技能查询

    public AgentSkill? GetClassSkillByName(string skillName)
    {
        foreach (var type in _skillTypes.Values)
        {
            try
            {
                var instance = (AgentSkill)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                if (instance.Frontmatter.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase))
                    return instance;
                (instance as IDisposable)?.Dispose();
            }
            catch { }
        }
        return null;
    }

    public IEnumerable<AgentSkill?> GetSkills()
    {
        foreach (var type in _skillTypes.Values)
        {
            yield return (AgentSkill)ActivatorUtilities.CreateInstance(_serviceProvider, type);
        }
    }

    public IEnumerable<AgentSkill> GetGeneralSkills()
    {
        foreach (var type in _skillTypes.Values)
        {
            if (_isGeneralCache.TryGetValue(type, out bool isGeneral) && isGeneral)
            {
                yield return (AgentSkill)ActivatorUtilities.CreateInstance(_serviceProvider, type);
            }
        }
    }

    public IEnumerable<string> GetAllSkillNames()
    {
        // 类技能名称
        var classNames = new List<string>();
        foreach (var type in _skillTypes.Values)
        {
            try
            {
                var instance = (AgentSkill)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                classNames.Add(instance.Frontmatter.Name);
            }
            catch { }
        }
        foreach (var name in classNames)
            yield return name;

        // 外部技能名称（从缓存读取）
        foreach (var name in _externalSkillNamesCache.Keys)
            yield return name;
    }

    public IEnumerable<string> GetExternalSkillDirectories()
    {
        if (!string.IsNullOrEmpty(_externalSkillsPath) && Directory.Exists(_externalSkillsPath))
        {
            foreach (var dir in Directory.GetDirectories(_externalSkillsPath))
            {
                yield return dir;
            }
        }
    }

    #endregion

    #region 外部技能缓存

    private void RefreshExternalSkillNames()
    {
        var newCache = new ConcurrentDictionary<string, byte>();
        if (Directory.Exists(_externalSkillsPath))
        {
            foreach (var dir in Directory.GetDirectories(_externalSkillsPath))
            {
                var mdFile = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(mdFile)) continue;
                var name = ParseSkillNameFromMarkdown(mdFile);
                if (!string.IsNullOrWhiteSpace(name))
                    newCache.TryAdd(name, 0);
            }
        }
        _externalSkillNamesCache = newCache;
    }

    private static string? ParseSkillNameFromMarkdown(string filePath)
    {
        try
        {
            var lines = File.ReadAllLines(filePath);
            bool inFrontMatter = false;
            foreach (var line in lines)
            {
                if (line.Trim() == "---")
                {
                    if (!inFrontMatter) inFrontMatter = true;
                    else break;
                    continue;
                }
                if (inFrontMatter && line.StartsWith("name:"))
                {
                    return line["name:".Length..].Trim();
                }
            }
        }
        catch { }
        return null;
    }

    #endregion

    public void Dispose()
    {
        _dllReloadCts?.Cancel();
        _externalReloadCts?.Cancel();
        _reloadLock.Dispose();
        _dllWatcher.Dispose();
        _externalMdWatcher.Dispose();
    }
}