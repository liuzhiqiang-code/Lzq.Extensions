---
name: lzq-extensions-eventbus-rabbitmq
description: 为 Lzq.Extensions.EventBus 提供基于 RabbitMQ 的集成事件发布器实现，支持可靠消息发送、持久化、死信队列与延迟消息（x-delayed-message 插件）。适用于需要使用 RabbitMQ 作为消息中间件来发布集成事件的 .NET 应用，与 EventBus 的 Outbox 或直接发布模式无缝配合。
license: Proprietary
compatibility: 需要 .NET 6+、Lzq.Extensions.EventBus、RabbitMQ.Client (7.2.0+)、RabbitMQ 服务器（支持 x-delayed-message 插件可选）
metadata:
  author: lzq
  version: "1.0"
  platform: AgentForge
---

## 概述

`Lzq.Extensions.EventBus.RabbitMq` 是 `Lzq.Extensions.EventBus` 的扩展库，实现了 `IIntegrationPublisher` 接口，使用 **RabbitMQ** 作为集成事件的消息代理。它封装了 RabbitMQ 的连接管理、交换机/队列声明、消息发布、死信机制和延迟消息支持（通过 `x-delayed-message` 插件）。

在以下场景使用本扩展：
- 已采用 `Lzq.Extensions.EventBus` 并需要将集成事件发布到 RabbitMQ。
- 需要可靠的持久化消息、死信队列、延迟消息等高级特性。
- 希望与 EventBus 的 Outbox 模式配合，实现事务一致性并最终由后台服务调用本发布器。

## 何时使用

- 项目集成了 `Lzq.Extensions.EventBus`，且需要实际的 MQ 实现来发送集成事件。
- 消息队列选型为 RabbitMQ。
- 需要至少一次交付、消息持久化、自动重连等企业级特性。
- 需要延迟消息功能（需 RabbitMQ 安装 `rabbitmq_delayed_message_exchange` 插件）。

## 何时不使用

- 不依赖 EventBus 集成事件，或使用其他 MQ（如 Kafka、Azure Service Bus）。
- 仅需进程内事件或简单测试场景（此时可用 `DefaultMemoryEventStore` 代替）。
- RabbitMQ 服务器无法安装延迟插件且需要延迟消息功能（本扩展默认使用 `x-delayed-message` 交换机类型，若未安装插件会失败，可修改代码使用普通 `direct` 交换机）。

## 集成步骤

### 1. 安装 NuGet 包

```bash
dotnet add package Lzq.Extensions.EventBus.RabbitMq
```

### 2. 配置 RabbitMQ 连接参数

在 `appsettings.json` 中（可选，也可以通过代码配置）：

``` json
{
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  }
}
```

### 3. 注册 EventBus 并添加 RabbitMQ 发布器

在 `Program.cs` 中：

``` csharp
using Lzq.Extensions.EventBus;
using Lzq.Extensions.EventBus.RabbitMq;

builder.Services.AddEventBus()
    .AddIntegrationEvent(integrationBuilder =>
    {
        // 添加 RabbitMQ 发布器（作为默认的 IIntegrationPublisher）
        integrationBuilder.AddRabbitMqPublisher(options =>
        {
            // 从配置读取
            builder.Configuration.GetSection("RabbitMq").Bind(options);
            // 或直接赋值
            // options.HostName = "my-rabbitmq";
            // options.Port = 5672;
            // options.UserName = "admin";
            // options.Password = "pass";
        });
    });
```

如果同时使用了 Outbox 存储（如数据库 Outbox），后台发布服务需要注入 `IIntegrationPublisher`，此时 RabbitMQ 发布器会被自动使用。

### 4. 使用示例

定义集成事件并发布：

``` csharp
public record OrderCreatedIntegrationEvent(Guid OrderId, decimal Amount) : BaseIntegrationEvent
{
    public override string TopicName => "order.events";
}

public class SomeService
{
    private readonly IEventBus _eventBus;
    public SomeService(IEventBus eventBus) => _eventBus = eventBus;

    public async Task CreateOrder()
    {
        // 业务逻辑...
        var evt = new OrderCreatedIntegrationEvent(Guid.NewGuid(), 100);
        
        // 发布集成事件（会通过 RabbitMqPublisher 发送）
        await _eventBus.PublishAsync(evt);
    }
}
```

## 工作原理

### 连接管理

- `RabbitMqPublisher` 维护一个长连接 (`IConnection`)，支持自动恢复 (`AutomaticRecoveryEnabled = true`)。
- 首次调用 `PublishAsync` 时建立连接，后续复用。
- 使用 `SemaphoreSlim` 确保线程安全。

### 交换机与队列拓扑

对于每个集成事件（`topicName`，默认取事件的 `TopicName` 属性或类名），发布器会声明以下基础设施：

| 组件                          | 名称规则                                                             | 用途                       |
| ------------------------------- | ---------------------------------------------------------------------- | ---------------------------- |
| 死信交换机（direct）          | `{topicName}_dead_letter_exchange`                                                                     | 接收无法路由的消息（可选） |
| 死信队列                      | `{topicName}_dead_letter_queue`                                                                     | 存储死信消息               |
| 主交换机（x-delayed-message） | `{topicName}`                                                                     | 支持延迟消息，类型为 `x-delayed-message`      |
| 主队列                        | `{topicName}_queue`                                                                     | 存储待消费消息             |
| 绑定关系                      | 主队列绑定到主交换机（routing key \= `{topicName}_routingKey`）<br />死信队列绑定到死信交换机 | 消息路由                   |

- 若不需要延迟消息，可修改代码将交换机类型改为 `ExchangeType.Direct`，并去掉 `x-delayed-type` 参数。
- 队列参数可配置：`x-max-length` 等（当前硬编码为 10000 条，超出则拒绝发布）。

### 消息发布流程

1. 获取或创建 `IConnection`。
2. 创建临时 `IChannel`。
3. 确保交换机和队列已声明（内部缓存 `_declaredTopics`）。
4. 序列化集成事件为 JSON（使用 `System.Text.Json`，无自定义选项）。
5. 设置消息属性（持久化、JSON 类型、消息ID、时间戳）。
6. 调用 `BasicPublishAsync` 发送到主交换机，routing key 为 `{topicName}_routingKey`。
7. 记录日志，异常时抛出。

### 死信机制

当消息无法被正常消费（如队列达到最大长度、消息被拒绝且不重新入队）时，RabbitMQ 会将消息转发到死信交换机，最终进入死信队列。应用可单独消费死信队列进行后续处理（如人工介入或记录日志）。

## 配置选项

`RabbitMqOptions` 类支持以下属性：

| 属性名 | 类型   | 默认值 | 说明            |
| -------- | -------- | -------- | ----------------- |
| `HostName`       | string | `localhost`       | RabbitMQ 主机名 |
| `Port`       | int    | `5672`       | 端口号          |
| `UserName`       | string | `guest`       | 用户名          |
| `Password`       | string | `guest`       | 密码            |
| `VirtualHost`       | string | `/`       | 虚拟主机        |

可通过 `IOptions<RabbitMqOptions>` 注入，或在 `AddRabbitMqPublisher` 回调中直接设置。

## 注意事项

1. **延迟消息插件依赖**：扩展默认使用 `x-delayed-message` 交换机类型。如果 RabbitMQ 服务器未安装该插件，启动时会报错 `unknown exchange type 'x-delayed-message'`。解决方案：

    - 安装插件：`rabbitmq-plugins enable rabbitmq_delayed_message_exchange`
    - 或修改源码，将交换机类型改为 `ExchangeType.Direct`，移除 `arguments` 中的 `x-delayed-type`。
2. **连接生命周期**：`RabbitMqPublisher` 注册为 **Singleton**，确保长连接复用。`Dispose` 方法在应用关闭时释放连接。
3. **线程安全**：`PublishAsync` 每次调用会新建 `IChannel`，不需要加锁，但连接建立有锁保护。RabbitMQ.Client 7.x 的 `IChannel` 不是线程安全的，但 `BasicPublishAsync` 本身是异步且每次新创建 channel 是安全的。
4. **事务一致性**：如果直接使用 `RabbitMqPublisher`（无 Outbox），消息发布不会参与数据库事务。如需强一致，请结合 Outbox 模式（将事件先存数据库，后台任务调用本发布器）。
5. **消息顺序**：RabbitMQ 在单队列内保证 FIFO 顺序，但多个事件发送到不同 Topic（交换机）时顺序不定。
6. **错误处理**：发布失败会抛出异常并记录日志。上层（如 Outbox 后台服务）应捕获异常并实现重试。

## 扩展点

### 自定义序列化

当前使用 `JsonSerializer.Serialize(@event, @event.GetType())`，默认无缩进、驼峰命名。如需定制，可继承 `RabbitMqPublisher` 并重写序列化逻辑。

### 控制交换机/队列参数

当前参数（队列长度限制、死信配置）是硬编码的。可以通过修改 `RabbitMqPublisher.DeclareExchangeAndQueueAsync` 方法或通过配置注入。建议将 `RabbitMqOptions` 扩展添加 `QueueArguments` 等属性。

### 支持发布确认（Publisher Confirms）

当前未启用发布确认，若需确保消息真正到达 broker，可基于 `IChannel.ConfirmSelectAsync()` 和 `WaitForConfirmsAsync` 扩展。但会增加延迟。

## 与其他 Lzq.Extensions.EventBus 组件的关系

- **主库**：`Lzq.Extensions.EventBus` 定义了 `IIntegrationPublisher` 接口，本扩展提供其 RabbitMQ 实现。
- **注册方式**：通过 `IntegrationEventBuilder.AddRabbitMqPublisher()` 添加。
- **Outbox 模式**：如果在 `AddIntegrationEvent` 中同时注册了 `IIntegrationEventStore`（如数据库 Outbox），则 `RabbitMqPublisher` 会被后台 `OutboxPublisherService` 调用，而不是立即调用。此时 `RabbitMqPublisher` 仍作为最终投递工具。

## 故障排查

| 问题                        | 可能原因                                 | 解决方案                           |
| ----------------------------- | ------------------------------------------ | ------------------------------------ |
| `unknown exchange type 'x-delayed-message'`                            | 未安装延迟消息插件                       | 安装插件或改用普通 direct 交换机   |
| 连接拒绝                    | RabbitMQ 未启动或网络不通                | 检查服务状态、主机名、端口         |
| 消息未出现在队列            | 交换机/队列绑定错误或 routing key 不匹配 | 检查声明代码，默认 routing key 为 `{topicName}_routingKey` |
| 重复发送（Outbox 后台服务） | 未实现消费者端幂等                       | 消费者根据 `EventId` 去重                   |

## 参考资料

- [RabbitMQ .NET Client 官方文档](https://rabbitmq.com/dotnet-api-guide.html)