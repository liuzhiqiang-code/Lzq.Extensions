using Masa.BuildingBlocks.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Lzq.Extensions.AI.AgentSkills;

public class AgentSkillProvider : IDisposable, IAsyncDisposable
{
    public string PluginPath => _pluginPath;
    public string ExternalSkillsPath => _externalSkillsPath;
    public void TriggerPluginReload() => ReloadSkills(CancellationToken.None);
    public void TriggerExternalSkillsRefresh() => RefreshExternalSkillNames();

    private readonly IServiceProvider _serviceProvider;
    private readonly string _pluginPath, _externalSkillsPath;
    private readonly FileSystemWatcher _dllWatcher, _externalMdWatcher;

    private volatile ConcurrentDictionary<string, Type> _skillTypes = new();
    private volatile ConcurrentDictionary<Type, bool> _isGeneralCache = new();
    private volatile ConcurrentDictionary<string, byte> _externalSkillNamesCache = new();
    private volatile ConcurrentDictionary<string, SkillCategory> _toolCategoryCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile ConcurrentDictionary<Type, string> _typeToSkillNameCache = new();
    private volatile ConcurrentDictionary<string, Type> _skillTypeNameMapping = new(StringComparer.OrdinalIgnoreCase);

    private SkillAssemblyLoadContext? _currentContext;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private int _isDisposed;
    private CancellationTokenSource? _dllReloadCts, _externalReloadCts;

    public AgentSkillProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _pluginPath = Path.Combine(AppContext.BaseDirectory, "AgentSkills");
        _externalSkillsPath = Path.Combine(AppContext.BaseDirectory, "ExternalSkills");

        Directory.CreateDirectory(_pluginPath);
        Directory.CreateDirectory(_externalSkillsPath);

        ReloadSkills(CancellationToken.None);
        RefreshExternalSkillNames();

        _dllWatcher = new FileSystemWatcher(_pluginPath, "*.dll") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        _externalMdWatcher = new FileSystemWatcher(_externalSkillsPath, "*.md") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName, IncludeSubdirectories = true, EnableRaisingEvents = true };

        SetupWatcherDebounce(_dllWatcher, ref _dllReloadCts, token => ReloadSkills(token), true);
        SetupWatcherDebounce(_externalMdWatcher, ref _externalReloadCts, _ => RefreshExternalSkillNames(), false);
    }

    private void SetupWatcherDebounce(FileSystemWatcher watcher, ref CancellationTokenSource? fieldCts, Action<CancellationToken> action, bool checkReady)
    {
        // 关键点：因为 ref 参数不能直接在局部函数（匿名方法）中使用，
        // 我们定义一个本地变量，通过 Lambda 间接或者在事件触发时通过字段安全访问。
        // 为了不破坏原有的 ref 交换逻辑，我们使用一个局部委托或者直接在内部通过局部分流处理：

        watcher.Changed += OnChanged; watcher.Created += OnChanged; watcher.Deleted += OnChanged;

        void OnChanged(object s, FileSystemEventArgs e)
        {
            if (Volatile.Read(ref _isDisposed) == 1) return;

            var newCts = new CancellationTokenSource();

            // 核心修正：利用 Interlocked 保证原子换引用，但这步必须在没有 ref 的作用域内发生。
            // 由于 ref 参数在 C# 闭包限制中严格，最地道的解法是将 fieldCts 转交或用底层指针/委托隔离。
            // 最稳妥不报错且保持精简的改法：改用一个 local 变量配合反射或在这里直接访问类的实例字段（如果它们是字段的话）。
            // 既然 fieldCts 是传入的 _dllReloadCts 或 _externalReloadCts 的引用，我们直接在外部对它做 Exchange：

            CancellationTokenSource? oldCts;
            if (checkReady)
            {
                // 如果是 DLL 监控
                oldCts = Interlocked.Exchange(ref _dllReloadCts, newCts);
            }
            else
            {
                // 如果是 Markdown 监控
                oldCts = Interlocked.Exchange(ref _externalReloadCts, newCts);
            }

            oldCts?.Cancel(); oldCts?.Dispose();
            var token = newCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    if (checkReady && e.ChangeType != WatcherChangeTypes.Deleted && !await WaitForFileReadyAsync(e.FullPath, 5, token)) return;
                    action(token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Console.WriteLine($"[LzqNet] 热重载后台任务故障: {ex.Message}"); }
            }, token);
        }
    }

    private void ReloadSkills(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) == 1) return;
        try { _reloadLock.Wait(cancellationToken); } catch (OperationCanceledException) { return; }

        try
        {
            if (cancellationToken.IsCancellationRequested) return;
            var nextSkillTypes = new ConcurrentDictionary<string, Type>();
            var nextIsGeneralCache = new ConcurrentDictionary<Type, bool>();
            var currentAssemblies = MasaApp.GetAssemblies();

            foreach (var assembly in currentAssemblies.Where(a => !a.FullName!.StartsWith("System") && !a.FullName.StartsWith("Microsoft") && !a.FullName.StartsWith("Masa")))
            {
                if (cancellationToken.IsCancellationRequested) return;
                ScanAssemblyTypes(assembly, nextSkillTypes, nextIsGeneralCache);
            }

            if (Directory.Exists(_pluginPath))
            {
                var newContext = new SkillAssemblyLoadContext();
                bool loadedAny = false;

                foreach (var file in Directory.GetFiles(_pluginPath, "*.dll"))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    try
                    {
                        if (currentAssemblies.Any(a => a.GetName().Name == Path.GetFileNameWithoutExtension(file))) continue;
                        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var pdbPath = Path.ChangeExtension(file, ".pdb");
                        var assembly = File.Exists(pdbPath)
                            ? newContext.LoadFromStream(fs, new FileStream(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            : newContext.LoadFromStream(fs);

                        ScanAssemblyTypes(assembly, nextSkillTypes, nextIsGeneralCache);
                        loadedAny = true;
                    }
                    catch (Exception ex) { Console.WriteLine($"[LzqNet] 加载外部插件 {file} 失败: {ex.Message}"); }
                }
                var oldContext = Interlocked.Exchange(ref _currentContext, loadedAny ? newContext : null);
                if (!loadedAny) newContext.Unload();
                oldContext?.Unload();
            }

            _skillTypes = nextSkillTypes;
            _isGeneralCache = nextIsGeneralCache;
            BuildToolAndNameCategoryCacheInternal(new ConcurrentDictionary<Type, string>(), cancellationToken);
        }
        finally { try { _reloadLock.Release(); } catch { } }
    }

    private void ScanAssemblyTypes(Assembly assembly, ConcurrentDictionary<string, Type> targetSkillTypes, ConcurrentDictionary<Type, bool> targetGeneralCache)
    {
        try
        {
            IEnumerable<Type> types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex) { var types = ex.Types.Where(t => t != null)!; }

        var filteredTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && IsSubclassOfRawGeneric(typeof(LzqAgentSkillBase<>), t));
        foreach (var type in filteredTypes)
        {
            targetSkillTypes.TryAdd(type.FullName!, type);
            targetGeneralCache[type] = type.GetCustomAttribute<GeneralSkillAttribute>()?.AutoLoad ?? false;
        }
    }

    private static bool IsSubclassOfRawGeneric(Type generic, Type? toCheck)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            if (generic == (toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck)) return true;
            toCheck = toCheck.BaseType;
        }
        return false;
    }

    public AgentSkillsProvider BuildAgentSkillsProviderBySelectedSkills(List<SkillMethodEntry> SelectedSkills)
    {
        var builder = new AgentSkillsProviderBuilder();
        foreach (var generalSkill in GetFilteredGeneralSkills(SelectedSkills)) builder.UseSkill(generalSkill);

        if (SelectedSkills?.Any() == true)
        {
            foreach (var entry in SelectedSkills)
                if (GetClassSkillByName(entry.SkillName) is { } skill) builder.UseSkill(skill);
        }

        if (Directory.Exists(_externalSkillsPath))
        {
            builder.UseFileSkill(_externalSkillsPath, scriptRunner: NoOpScriptRunner)
                   .UseFilter(skill => SelectedSkills?.Any() != true || SelectedSkills.Any(s => s.SkillName.Equals(skill.Frontmatter.Name, StringComparison.OrdinalIgnoreCase)));
        }
        return builder.UseOptions(options => options.DisableCaching = true).Build();
    }

    private static Task<object?> NoOpScriptRunner(AgentFileSkill skill, AgentFileSkillScript script, JsonElement? args, IServiceProvider? sp, CancellationToken ct)
        => Task.FromResult<object?>($"脚本已禁用: {script.Name}");

    public AgentSkill? GetClassSkillByName(string skillName)
        => !string.IsNullOrWhiteSpace(skillName) && _skillTypeNameMapping.TryGetValue(skillName, out var type)
            ? (AgentSkill?)ActivatorUtilities.CreateInstance(_serviceProvider, type) : null;

    public IEnumerable<AgentSkill?> GetSkills() => _skillTypes.Values.Select(t => (AgentSkill?)ActivatorUtilities.CreateInstance(_serviceProvider, t));

    private IEnumerable<AgentSkill> GetFilteredGeneralSkills(List<SkillMethodEntry> selectedSkills)
    {
        var selectedNames = selectedSkills?.Select(s => s.SkillName).Where(n => !string.IsNullOrEmpty(n)).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentGeneralCache = _isGeneralCache;
        var currentTypeToName = _typeToSkillNameCache;

        foreach (var type in _skillTypes.Values)
        {
            if (currentGeneralCache.TryGetValue(type, out bool isGeneral) && isGeneral)
            {
                var attr = type.GetCustomAttribute<GeneralSkillAttribute>();
                if (attr == null) continue;
                if (attr.AutoLoad || (currentTypeToName.TryGetValue(type, out var regName) && selectedNames.Contains(regName)))
                {
                    if (ActivatorUtilities.CreateInstance(_serviceProvider, type) is AgentSkill instance) yield return instance;
                }
            }
        }
    }

    public IEnumerable<string> GetAllSkillNames() => _skillTypeNameMapping.Keys.Concat(_externalSkillNamesCache.Keys);
    public IEnumerable<string> GetExternalSkillDirectories() => Directory.Exists(_externalSkillsPath) ? Directory.GetDirectories(_externalSkillsPath) : Array.Empty<string>();

    private void RefreshExternalSkillNames()
    {
        if (Volatile.Read(ref _isDisposed) == 1) return;
        _reloadLock.Wait();
        try
        {
            var newCache = new ConcurrentDictionary<string, byte>();
            if (Directory.Exists(_externalSkillsPath))
            {
                foreach (var dir in Directory.GetDirectories(_externalSkillsPath))
                {
                    var mdFile = Path.Combine(dir, "SKILL.md");
                    if (File.Exists(mdFile) && ParseSkillNameFromMarkdown(mdFile) is { } name) newCache.TryAdd(name, 0);
                }
            }
            _externalSkillNamesCache = newCache;
            BuildToolAndNameCategoryCacheInternal(new ConcurrentDictionary<Type, string>(), CancellationToken.None);
        }
        finally { try { _reloadLock.Release(); } catch { } }
    }

    private static string? ParseSkillNameFromMarkdown(string filePath)
    {
        try
        {
            using var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            bool inFrontMatter = false; string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim() == "---") { if (!inFrontMatter) inFrontMatter = true; else break; continue; }
                if (inFrontMatter && line.StartsWith("name:")) return line["name:".Length..].Trim();
            }
        }
        catch { }
        return null;
    }

    private void BuildToolAndNameCategoryCacheInternal(ConcurrentDictionary<Type, string> nextTypeToSkillNameCache, CancellationToken cancellationToken)
    {
        var nextToolCategoryCache = new ConcurrentDictionary<string, SkillCategory>(StringComparer.OrdinalIgnoreCase);
        var nextSkillTypeNameMapping = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in _skillTypes.Values)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                var category = type.GetCustomAttribute<GeneralSkillAttribute>()?.Category ?? SkillCategory.Core;
                if (ActivatorUtilities.CreateInstance(_serviceProvider, type) is AgentSkill instance && instance.Frontmatter?.Name is { } skillName)
                {
                    nextToolCategoryCache[skillName] = category;
                    nextSkillTypeNameMapping[skillName] = type;
                    nextTypeToSkillNameCache[type] = skillName;

                    if (instance is IAsyncDisposable ad) FireAndForgetDispose(ad);
                    else if (instance is IDisposable d) d.Dispose();
                }
            }
            catch (Exception ex) { Console.WriteLine($"[LzqNet] 解析内置插件缓存异常 ({type.Name}): {ex.Message}"); }
        }

        foreach (var extSkillName in _externalSkillNamesCache.Keys) nextToolCategoryCache.TryAdd(extSkillName, SkillCategory.Core);

        _toolCategoryCache = nextToolCategoryCache;
        _skillTypeNameMapping = nextSkillTypeNameMapping;
        _typeToSkillNameCache = nextTypeToSkillNameCache;
    }

    private static void FireAndForgetDispose(IAsyncDisposable disposable) => Task.Run(async () => { try { await disposable.DisposeAsync().ConfigureAwait(false); } catch { } });

    public SkillCategory GetCategoryByToolName(string toolName)
        => !string.IsNullOrWhiteSpace(toolName) && _toolCategoryCache.TryGetValue(toolName, out var category) ? category : SkillCategory.Core;

    private static async Task<bool> WaitForFileReadyAsync(string filename, int maxRetries, CancellationToken token)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try { if (token.IsCancellationRequested) return false; using var stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
            catch (IOException) { await Task.Delay(200, token); }
        }
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
        _dllWatcher.Dispose(); _externalMdWatcher.Dispose();
        _dllReloadCts?.Cancel(); _externalReloadCts?.Cancel();
        _reloadLock.Dispose(); _currentContext?.Unload();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;
        _dllWatcher.Dispose(); _externalMdWatcher.Dispose();
        if (_dllReloadCts != null) await _dllReloadCts.CancelAsync();
        if (_externalReloadCts != null) await _externalReloadCts.CancelAsync();
        _reloadLock.Dispose(); _currentContext?.Unload();
        GC.SuppressFinalize(this);
    }

    private sealed class SkillAssemblyLoadContext : AssemblyLoadContext { public SkillAssemblyLoadContext() : base(isCollectible: true) { } protected override Assembly? Load(AssemblyName name) => null; }
}