using Masa.BuildingBlocks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Lzq.Core.Modules;

public class SerializationModuleBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private List<Type> _sortedModuleTypes = new();
    private List<IModule> _modules = new();
    private ILogger? _logger;

    internal SerializationModuleBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    public SerializationModuleBuilder ConfigureModules<TStartup>() where TStartup : IModule
    {
        var resolver = new ModuleDependencyResolver();
        _sortedModuleTypes = resolver.Resolve<TStartup>();

        var assemblies = _sortedModuleTypes
            .Select(t => t.Assembly)
            .Distinct()
            .ToArray();
        MasaApp.TryAddAssemblies(assemblies);
        _services.AddAutoInject(MasaApp.GetAssemblies());

        _modules = _sortedModuleTypes
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToList();

        var serviceProvider = _services.BuildServiceProvider();
        _logger = serviceProvider.GetRequiredService<ILogger<SerializationModuleBuilder>>();

        LogDependencyTree();

        var ctx = new ModuleServiceContext(_services, _configuration);
        ExecutePhase("PreConfigureServices", m => m.PreConfigureServices(ctx));
        ExecutePhase("ConfigureServices", m => m.ConfigureServices(ctx));
        ExecutePhase("PostConfigureServices", m => m.PostConfigureServices(ctx));

        _logger?.LogInformation("Module configuration completed successfully");
        return this;
    }

    internal void Initialize(WebApplication app)
    {
        _logger ??= app.Services.GetRequiredService<ILogger<SerializationModuleBuilder>>();

        var ctx = new ModuleInitContext(app, _logger!);
        ExecutePhase("OnPreApplicationInitialization", m => m.OnPreApplicationInitialization(ctx));
        ExecutePhase("OnApplicationInitialization", m => m.OnApplicationInitialization(ctx));
        ExecutePhase("OnPostApplicationInitialization", m => m.OnPostApplicationInitialization(ctx));

        _logger?.LogInformation("Application initialization completed");
    }

    internal void RegisterSelf(IServiceCollection services)
    {
        services.AddSingleton(this);
    }

    private void LogDependencyTree()
    {
        _logger?.LogInformation("========== Module Load Order (dependency-first) ==========");

        var depLookup = new Dictionary<string, string[]>();
        foreach (var type in _sortedModuleTypes)
        {
            var deps = type.GetCustomAttributes<DependsOnAttribute>()
                .SelectMany(a => a.ModuleTypes)
                .Where(t => _sortedModuleTypes.Contains(t))
                .Select(t => t.Name)
                .ToArray();
            depLookup[type.Name] = deps;
        }

        for (int i = 0; i < _sortedModuleTypes.Count; i++)
        {
            var name = _sortedModuleTypes[i].Name;
            var deps = depLookup[name];
            var isLast = i == _sortedModuleTypes.Count - 1;
            var prefix = isLast ? "\u2514\u2500" : "\u251c\u2500";

            if (deps.Length == 0)
                _logger?.LogInformation("{Prefix} {Index}. {Module} [root]", prefix, i + 1, name);
            else
                _logger?.LogInformation("{Prefix} {Index}. {Module}", prefix, i + 1, name);

            if (deps.Length > 0)
            {
                var depPrefix = isLast ? "   " : "\u2502  ";
                _logger?.LogInformation("{DepPrefix}\u2514\u2500 Depends on: {Deps}", depPrefix, string.Join(", ", deps));
            }
        }
    }

    private void ExecutePhase(string phaseName, Action<IModule> action)
    {
        _logger?.LogInformation("---------- {PhaseName} ----------", phaseName);
        foreach (var module in _modules)
        {
            _logger?.LogDebug("  {Module}.{PhaseName}", module.GetType().Name, phaseName);
            action(module);
        }
    }
}
