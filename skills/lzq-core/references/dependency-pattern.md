# 依赖注入与模块注册

## 自动注入规则

`AddCoreAutoInject()` 按约定注册服务：

| 接口 | 生命周期 | 使用场景 |
|---|---|---|
| `ISingletonDependency` | Singleton | 无状态工具类 |
| `IScopedDependency` | Scoped | 业务服务（推荐） |
| `ITransientDependency` | Transient | 轻量临时服务 |

## 模块注册

每个业务模块提供 `AddXxxModule` 扩展方法，然后由宿主统一调用。

``` csharp
public static class XxxModuleExtensions
{
    public static IServiceCollection AddXxxModule(this IServiceCollection services)
    {
        services.AddCoreAssembly("Lzq.Xxx.");
        services.AddCoreAutoInject();
        return services;
    }
}
```

## Program.cs 顶层注册 

``` csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddCoreAssembly("Lzq.")       // 加载所有 Lzq.* 程序集
    .AddMapster()                   // 注册 Mapster 映射
    .AddCoreAutoInject();           // 自动注入

builder.Services.AddCoreMinimalAPIs(); // 注册 Masa MinimalAPIs

var app = builder.Build();

app.UseCoreExceptionHandler();
app.MapMasaMinimalAPIs();

app.Run();
```

## ServiceBase 依赖获取

在 `ServiceBase` 子类中通过 `GetRequiredService` 和 `GetService` 获取依赖。

``` csharp
public class XxxService : ServiceBase
{
    // 必需服务（推荐使用，若缺失会立即抛出异常）
    private IRepository<XxxEntity> Repo => GetRequiredService<IRepository<XxxEntity>>();
    private ILogger<XxxService> Logger => GetRequiredService<ILogger<XxxService>>();
    private ICurrentUser CurrentUser => GetRequiredService<ICurrentUser>();

    // 可选服务
    private IOptionalService? Opt => GetService<IOptionalService>();
}
```

## 依赖生命周期选择

| 类型         | 示例 | 建议生命周期            |
| -------------- | ------ | ------------------------- |
| 仓储         | `IRepository<T>`     | Scoped                  |
| 数据库上下文 | `ISqlSugarClient`     | Scoped                  |
| 日志         | `ILogger<T>`     | Singleton（由框架管理） |
| 当前用户     | `ICurrentUser`     | Scoped                  |
| 工具类       | `IStringHelper`     | Singleton               |
| 远程服务代理 | `IExternalApiClient`     | Scoped 或 Transient     |

## 模块组织建议

``` text
src/
├── Lzq.Xxx.Application.Contracts/   # DTO, 接口
├── Lzq.Xxx.Application.Services/     # Service 实现
├── Lzq.Xxx.Domain/                   # 实体, 仓储接口
└── Lzq.Xxx.Infrastructure/           # 仓储实现, 外部服务
```

每个项目在自己的 `ServiceCollectionExtensions` 中注册依赖，宿主角只需调用顶层 `AddXxxModule()`。