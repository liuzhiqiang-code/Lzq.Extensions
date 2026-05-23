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
    private readonly HashSet<Type> _registered = new();
    private readonly List<IModule> _modules = new();
    private ILogger? _logger;

    internal SerializationModuleBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    public SerializationModuleBuilder AddModule<T>() where T : IModule, new()
    {
        var type = typeof(T);

        if (_registered.Contains(type))
            return this;

        CheckDependsOn(type);
        var module = new T();

        _modules.Add(module);
        _registered.Add(type);

        return this;
    }

    private void CheckDependsOn(Type type)
    {
        var allDependencies = type.GetCustomAttributes<DependsOnAttribute>()
            .SelectMany(attr => attr.ModuleTypes)   // 收集所有 Attribute 声明的依赖
            .Distinct();                            // 去重，避免重复检查

        foreach (var dep in allDependencies)
        {
            if (!_registered.Contains(dep))
            {
                throw new LzqModuleException(
                    $"模块 {type.Name} 依赖 {dep.Name}，请先通过 AddModule<{dep.Name}>() 注册");
            }
        }
    }

    /// <summary>
    /// 在所有模块注册完成后调用，执行选项预配置和服务配置阶段
    /// </summary>
    public SerializationModuleBuilder ConfigureModules()
    {
        // 1. 选项预绑定（Configure）
        var configureContext = new ModuleConfigureContext(_services, _configuration);
        foreach (var module in _modules)
        {
            module.Configure(configureContext);
        }

        // 2. 构建临时 ServiceProvider，以便在 ConfigureServices 中使用 IOptions<T> 等
        var serviceProvider = _services.BuildServiceProvider();
        _logger = serviceProvider.GetRequiredService<ILogger<SerializationModuleBuilder>>();

        // 3. 服务配置三阶段
        _logger?.LogDebug("Executing PreConfigureServices / ConfigureServices / PostConfigureServices");
        var ctx = new ModuleServiceContext(_services, _configuration, serviceProvider);
        foreach (var module in _modules)
        {
            var moduleName = module.GetType().Name;
            _logger?.LogDebug("  - PreConfigureServices on {ModuleName}", moduleName);
            module.PreConfigureServices(ctx);
        }
        foreach (var module in _modules)
        {
            var moduleName = module.GetType().Name;
            _logger?.LogDebug("  - ConfigureServices on {ModuleName}", moduleName);
            module.ConfigureServices(ctx);
        }
        foreach (var module in _modules)
        {
            var moduleName = module.GetType().Name;
            _logger?.LogDebug("  - PostConfigureServices on {ModuleName}", moduleName);
            module.PostConfigureServices(ctx);
        }
        _logger?.LogInformation("Module configuration phase completed");
        return this;
    }

    internal void Initialize(WebApplication app)
    {
        _logger?.LogInformation("Starting application initialization phase");

        var ctx = new ModuleInitContext(app, _logger!);
        foreach (var module in _modules)
        {
            var moduleName = module.GetType().Name;
            _logger?.LogDebug("  - OnPreApplicationInitialization {ModuleName}", moduleName);
            module.OnPreApplicationInitialization(ctx);
        }
        foreach (var module in _modules)
        {
            var moduleName = module.GetType().Name;
            _logger?.LogDebug("  - OnApplicationInitialization {ModuleName}", moduleName);
            module.OnApplicationInitialization(ctx);
        }
        foreach (var module in _modules)
        {
            var moduleName = module.GetType().Name;
            _logger?.LogDebug("  - OnPostApplicationInitialization {ModuleName}", moduleName);
            module.OnPostApplicationInitialization(ctx);
        }
        _logger?.LogInformation("Application initialization completed");
    }

    internal void RegisterSelf(IServiceCollection services)
    {
        services.AddSingleton(this);
    }
}