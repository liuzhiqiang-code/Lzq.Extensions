using Masa.BuildingBlocks.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Reflection;

namespace Lzq.Extensions.AI.AgentSkills;

public class AgentSkillProvider : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _pluginPath;
    private readonly FileSystemWatcher _watcher;
    // 缓存加载的类型，Key 为类全名
    private readonly ConcurrentDictionary<string, Type> _skillTypes = new();

    public AgentSkillProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _pluginPath = Path.Combine(AppContext.BaseDirectory, "AgentSkills");

        if (!Directory.Exists(_pluginPath)) Directory.CreateDirectory(_pluginPath);

        // 初始化加载
        ReloadSkills();

        // 配置监听器
        _watcher = new FileSystemWatcher(_pluginPath, "*.dll");
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
        _watcher.Changed += (s, e) => ReloadSkills();
        _watcher.Created += (s, e) => ReloadSkills();
        _watcher.Deleted += (s, e) => ReloadSkills();
        _watcher.EnableRaisingEvents = true;
    }

    private void ReloadSkills()
    {
        Console.WriteLine("[LzqNet] 正在刷新插件列表...");
        _skillTypes.Clear();

        // 扫描程序启动自带程序集
        var currentAssemblies = MasaApp.GetAssemblies();
        foreach (var assembly in currentAssemblies)
        {
            // 过滤掉系统程序集以提高性能（可选）
            if (assembly.FullName!.StartsWith("System") || assembly.FullName.StartsWith("Microsoft") || assembly.FullName.StartsWith("Masa")) 
                continue;

            ScanAssemblyTypes(assembly);
        }

        // --- 2. 扫描外部目录 DLL ---
        if (Directory.Exists(_pluginPath))
        {
            var files = Directory.GetFiles(_pluginPath, "*.dll");
            foreach (var file in files)
            {
                try
                {
                    // 检查是否已经通过 MasaApp 加载了同名程序集
                    var assemblyName = Path.GetFileNameWithoutExtension(file);
                    if (currentAssemblies.Any(a => a.GetName().Name == assemblyName))
                        continue;

                    byte[] assemblyBytes = File.ReadAllBytes(file);

                    // --- 关键：尝试加载 PDB 调试符号文件 ---
                    var pdbPath = Path.ChangeExtension(file, ".pdb");
                    Assembly assembly;
                    if (File.Exists(pdbPath))
                    {
                        byte[] pdbBytes = File.ReadAllBytes(pdbPath);
                        // 同时载入 DLL 和 PDB，调试器就能找到源码行号
                        assembly = Assembly.Load(assemblyBytes, pdbBytes);
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

    /// <summary>
    /// 统一的类型扫描逻辑
    /// </summary>
    private void ScanAssemblyTypes(Assembly assembly)
    {
        try
        {
            var types = assembly.GetTypes().Where(t =>
                t is { IsClass: true, IsAbstract: false } &&
                IsSubclassOfRawGeneric(typeof(LzqAgentSkillBase<>), t));

            foreach (var type in types)
            {
                // 使用 TryAdd 确保同名类（如果内外冲突）不会导致崩溃
                _skillTypes.TryAdd(type.FullName!, type);
            }
        }
        catch
        {
            // 忽略某些无法导出类型的程序集
        }
    }

    /// <summary>
    /// 获取所有技能实例（实时实例化，支持 DI）
    /// </summary>
    public IEnumerable<object> GetSkills()
    {
        foreach (var type in _skillTypes.Values)
        {
            // 使用 ActivatorUtilities 自动从 DI 容器中寻找构造函数参数（如 ISqlSugarClient）
            yield return ActivatorUtilities.CreateInstance(_serviceProvider, type);
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

    public void Dispose() => _watcher.Dispose();
}