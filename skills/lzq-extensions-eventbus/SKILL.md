---
name: lzq-extensions-eventbus
description: 使用 Lzq.Extensions.EventBus 库在 .NET 应用中实现 CQRS 模式、进程内事件与集成事件，基于 MediatR 提供管道行为（日志、验证、事务、集成事件拦截），支持 Outbox 模式与声明式事件处理。适用于需要命令查询职责分离、事件驱动架构以及可靠集成事件发布的场景。
license: Proprietary
compatibility: 需要 .NET 6+、Lzq.Extensions.EventBus NuGet 包、MediatR 14.1.0+、FluentValidation、Lzq.Core 基础库
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.EventBus` 基于 **MediatR** 封装，为 .NET 应用提供开箱即用的 CQRS 与事件驱动能力。它定义了 `Command`（命令）、`Query`（查询）、`LocalEvent`（进程内事件）和 `IntegrationEvent`（集成事件）四种消息契约，通过 MediatR 管道自动注入日志记录、输入验证、事务管理以及集成事件的拦截与分发。集成事件支持 Outbox 模式（可通过 `IIntegrationEventStore` 持久化），确保在事务提交后可靠发布。

在以下场景使用本技能：
- 需要实现 CQRS 分离，将写操作（Command）与读操作（Query）解耦。
- 需要在领域事件或本地事件发生时，自动触发集成事件的发布。
- 需要透明的事务管理：通过 `[UnitOfWork]` 特性自动开启/提交/回滚数据库事务。
- 需要自动校验 Command/Query 输入（基于 FluentValidation）。
- 需要一套标准的管道行为来记录日志、控制事务边界。

## 何时使用

- 提到 `Lzq.Extensions.EventBus`、`AddEventBus`、`IEventBus`、`ICommand`、`ILocalEvent`。
- 用户希望“使用 MediatR 实现 CQRS”、“添加事件总线”、“集成事件 Outbox”。
- 需要管道行为（日志、验证、事务）统一管理。
- 需要将内部事件转换为集成事件并保证至少一次投递。

## 何时不使用

- 简单的 CRUD 应用，无需命令/查询分离和管道行为。
- 已使用其他 CQRS 框架（如 Brighter、MassTransit），且无迁移需求。
- 不需要进程内事件或集成事件。
- 应用不依赖 `MediatR` 或不想引入额外的抽象。

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.EventBus
```

传递依赖（自动安装）：

- `MediatR` (\>\= 14.1.0)
- `FluentValidation`
- `Lzq.Core`（提供 `IUnitOfWork`、`ICurrentUser`、`MasaApp` 等基础能力）
- `Microsoft.AspNetCore.App` 框架引用（提供 `IServiceProvider` 等）

### 2. 注册服务

在 `Program.cs` 中注册：

``` csharp
using Lzq.Extensions.EventBus;

builder.Services.AddEventBus();
```

此方法会执行：

- 扫描 `MasaApp.GetAssemblies()` 中的所有程序集，注册 MediatR 的请求/通知处理器。
- 按顺序添加管道行为：`LoggingBehavior` → `ValidatorBehavior` → `TransactionBehavior` → `IntegrationEventMiddleware`。
- 自动发现并注册 FluentValidation 验证器。
- 注册 `IEventBus`（实现 `MediatREventBus`）为 Scoped 服务。

### 3. 定义命令、查询与事件

**命令（Command）** ：修改数据，可无返回或返回结果。

``` csharp
public record CreateUserCommand(string Name, string Email) : BaseCommand<long>;
// BaseCommand<long> 实现了 ICommand<long>
```

**查询（Query）** ：只读操作，必须返回结果。

``` csharp
public record GetUserQuery(long UserId) : BaseQuery<UserDto>;
```

**进程内事件（LocalEvent）** ：在同一进程内通知多个处理器。

``` csharp
public record UserCreatedEvent(long UserId, string Name) : BaseLocalEvent;
```

**集成事件（IntegrationEvent）** ：需要跨服务发布的事件（继承自 `ILocalEvent`）。
 
``` csharp
public record UserCreatedIntegrationEvent(long UserId, string Name) : BaseIntegrationEvent
{
    public override string TopicName => "User.Created";
}
```

所有消息都自动包含 `EventId` (Guid) 和 `CreateTime` (DateTime)。

### 4. 编写处理器

**命令/查询处理器**：实现 `IRequestHandler<TRequest, TResponse>`。

``` csharp
public class CreateUserHandler : IRequestHandler<CreateUserCommand, long>
{
    private readonly IUnitOfWork _uow;
    public CreateUserHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<long> Handle(CreateUserCommand cmd, CancellationToken ct)
    {
        // 业务逻辑：创建用户，返回 Id
        var userId = ...;
        return userId;
    }
}
```

**事件处理器**：实现 `INotificationHandler<TEvent>`。
 
``` csharp
public class UserCreatedEventHandler : INotificationHandler<UserCreatedEvent>
{
    public Task Handle(UserCreatedEvent notification, CancellationToken ct)
    {
        // 处理用户创建后的操作：发送邮件等
        return Task.CompletedTask;
    }
}
```

### 5. 使用 `IEventBus` 门面

``` csharp
public class UserService
{
    private readonly IEventBus _bus;
    public UserService(IEventBus bus) => _bus = bus;

    public async Task<long> CreateUser(string name, string email)
    {
        // 发送命令
        var userId = await _bus.SendAsync(new CreateUserCommand(name, email));
        
        // 发布本地事件（将触发所有 INotificationHandler<UserCreatedEvent>）
        await _bus.PublishAsync(new UserCreatedEvent(userId, name));
        
        // 发布集成事件（自动进入 Outbox 或直接发布，取决于配置）
        await _bus.PublishAsync(new UserCreatedIntegrationEvent(userId, name));
        
        return userId;
    }
}
```

### 6. 管道行为详解

#### 6.1 日志记录

每个命令/查询/事件处理前后都会记录日志（级别 Information），包含耗时。

#### 6.2 输入验证

如果针对某命令/查询定义了 FluentValidation 验证器，则自动验证。不通过时抛出 `MasaValidatorException`。

验证器示例：

``` csharp
public class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).EmailAddress();
    }
}
```

#### 6.3 事务管理

- 在 Command 类上添加 `[UnitOfWork]` 特性，方法执行前自动开启事务（默认 ReadCommitted 隔离级别），成功提交，失败回滚。
- 如果已有事务打开（`IUnitOfWork.IsAnyTran()` 返回 true），则跳过。
- `IQuery<out TResponse>` 类型的请求**不会**触发事务。

``` csharp
[UnitOfWork]
public record CreateOrderCommand : BaseCommand<long>;
```

### 7. 集成事件与 Outbox 模式

当通过 `IEventBus.PublishAsync` 发布一个 `IIntegrationEvent` 时，`IntegrationEventMiddleware` 拦截：

- **若未注册 Outbox（默认）** ：尝试从容器获取 `IIntegrationPublisher` 并直接发布。若也未注册，则抛出异常。
- **若注册了 Outbox**（通过 `UserMemoryOutbox` 或自定义持久化实现）：事件被保存到 `IIntegrationEventStore`，后续由独立进程（如后台任务）读取并发布，确保事务一致性。

**启用内存 Outbox（仅测试用）** ：

``` csharp
builder.Services.AddEventBus()
    .AddIntegrationEvent(builder => builder.UseMemoryOutbox());
```

生产环境请实现基于数据库的 `IIntegrationEventStore`（如使用 SqlSugar 的持久化 Outbox），注册为 Scoped 服务。

### 8. 事务与集成事件的协同

管道顺序保证事务先执行。因此，如果命令处理器上标注了 `[UnitOfWork]`，事务成功提交后，集成事件才被拦截并保存至 Outbox（或发布）。若抛出异常，事务回滚，事件不会被保存。

## 核心接口和基类

| 抽象     | 说明                |
| ---------- | --------------------- |
| `IMessage`         | 基础消息，提供 `EventId` 和 `CreateTime` |
| `ICommand<TResponse>`         | 有返回值的命令      |
| `ICommand`         | 无返回值的命令      |
| `IQuery<TResponse>`         | 查询                |
| `ILocalEvent`         | 进程内事件（多播）  |
| `IIntegrationEvent`         | 集成事件，额外提供 `TopicName` |
| `BaseCommand`, `BaseCommand<TResponse>`, `BaseQuery<TResponse>`, `BaseLocalEvent`, `BaseIntegrationEvent` | 对应基类            |

## 配置参考

无 `appsettings.json` 强制配置。所有行为通过代码注册和特性驱动。

- **`AddEventBus()`** ：核心注册方法，自动扫描程序集。
- **`EventBusBuilder.AddIntegrationEvent(configure)`** ：进一步配置集成事件行为，例如指定 Outbox 实现。
- **`IntegrationEventBuilder.UseMemoryOutbox()`** ：使用内存队列作为演示版 Outbox。

## 注意事项

- **程序集扫描**：默认使用 `MasaApp.GetAssemblies()`。请确保在调用 `AddEventBus` 前调用 `builder.AddLzqMasaAssembly()` 或其他方式加载所需程序集。
- **事务依赖**：`TransactionBehavior` 需要 DI 容器中注册 `IUnitOfWork` 的实现（例如来自 `Lzq.Extensions.SqlSugar` 的 `SqlSugarUnitWork`）。
- **验证器注册**：FluentValidation 验证器必须实现 `IValidator<TRequest>` 并被程序集扫描发现。
- **集成事件拦截**：`IntegrationEventMiddleware` 直接返回 `default!`，因此 `SendAsync` 或 `PublishAsync` 的返回值被忽略（发送集成事件不应期待返回值）。建议使用 `PublishAsync` 发送集成事件。
- **Outbox 持久化**：默认未注册任何 `IIntegrationEventStore`，直接发布依赖 `IIntegrationPublisher`。生产环境建议使用数据库 Outbox 并实现 `IIntegrationEventStore`。
- **管道顺序**：固定为 日志 → 验证 → 事务 → 集成事件。不可自定义顺序，除非修改库源码。

## 扩展点

- **自定义管道行为**：可继承 `IPipelineBehavior<,>` 并在注册时通过 `cfg.AddOpenBehavior` 添加（但默认扩展方法已固定）。
- **替换 Outbox 实现**：实现 `IIntegrationEventStore`，注册为 Scoped 服务即可替换内存版。
- **自定义集成事件发布器**：实现 `IIntegrationPublisher`，通过 DI 注册，即可在无 Outbox 时直接发布（如通过 RabbitMQ、Kafka）。

## 参考资料

- `references/eventbus-pipeline-details.md` — 管道行为详解