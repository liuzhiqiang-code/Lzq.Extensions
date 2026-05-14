# EventBus 管道详情参考 — `Lzq.Extensions.EventBus`

本文档深入说明 `Lzq.Extensions.EventBus` 中四种内置管道行为（LoggingBehavior、ValidatorBehavior、TransactionBehavior、IntegrationEventMiddleware）的触发条件、执行顺序、异常传播策略以及它们之间的协作方式。

## 1. 管道顺序与注册

在 `AddEventBus()` 中，管道按以下顺序注册：

1. `LoggingBehavior<TRequest, TResponse>`
2. `ValidatorBehavior<TRequest, TResponse>`
3. `TransactionBehavior<TRequest, TResponse>`
4. `IntegrationEventMiddleware<TRequest, TResponse>`

MediatR 会按照注册顺序依次执行管道，请求先进入 1，再到 2、3、4，最终到达实际 `IRequestHandler`，响应则按相反顺序返回。

## 2. LoggingBehavior

### 2.1 触发条件
- **所有**请求（Command、Query、LocalEvent、IntegrationEvent）都会触发日志记录。
- 任何通过 `IEventBus.SendAsync/PublishAsync` 发送的消息都会经过此管道。

### 2.2 行为
- **请求前**：记录 `[EventBus] 开始执行 {CommandName} {@CommandContent}`，级别 Information。
- **请求后**（无论成功或异常）：记录 `[EventBus] 执行结束 {CommandName}, 耗时: {Elapsed}ms`，级别 Information。

### 2.3 异常处理
- 不捕获或处理异常，异常会直接向上传播到下一个管道或 MediatR 调用方。
- 即使处理过程中抛出异常，`stopwatch` 依然停止，并记录结束日志（因为 `await next()` 在 try 块外）。

### 2.4 性能考量
- 使用 `Stopwatch` 准确计时，开销极低。
- 日志消息中包含 `{@CommandContent}`（序列化整个请求），生产环境需注意数据敏感性和序列化开销，可通过日志级别或过滤控制。

## 3. ValidatorBehavior

### 3.1 触发条件
- 仅当 DI 容器中存在**至少一个** `IValidator<TRequest>` 注册时才会执行验证；否则直接调用 `next()`。
- 请求类型**无限制**：任何实现了 `IValidator<TRequest>` 的请求都会被验证。

### 3.2 行为
- 收集所有注册的验证器，并行执行验证（`Task.WhenAll`）。
- 汇总所有验证错误，若存在 `failures` 则：
  - 记录日志 `[EventBus] 参数校验失败 {CommandName}: {@Errors}`，级别 Warning。
  - 抛出 `MasaValidatorException`，消息包含全部错误信息。

### 3.3 异常传播
- 抛出 `MasaValidatorException` 后，后续管道（Transaction、IntegrationEvent）**不会执行**。
- 调用方可以在最外层捕获该异常并转换为 HTTP 400 响应。

### 3.4 设计说明
- `MasaValidatorException` 来自 `Lzq.Core` 或 Masa 生态，通常可被全局异常中间件捕获。
- 如需自定义异常类型，可修改 `ValidatorBehavior` 源码。

## 4. TransactionBehavior

### 4.1 触发条件
- 仅处理 **非查询类** 请求（排除了实现 `IQuery<out T>` 的查询）。
- 必须同时满足：
  - 请求类型上标记了 `[UnitOfWork]` 特性。
  - 当前不存在已开启的事务（`unitOfWork.IsAnyTran() == false`）。
- 若以上任一条件不满足，直接调用 `next()` 跳过事务。

### 4.2 行为
- **事务开启**：调用 `unitOfWork.BeginTranAsync(attr.IsolationLevel)`，隔离级别默认 `ReadCommitted`。
- **执行处理**：调用 `next()` 进入后续管道（IntegrationEventMiddleware）和实际 Handler。
- **事务提交**：若无异常，调用 `unitOfWork.CommitTranAsync()`。
- **事务回滚**：捕获任何异常后，调用 `unitOfWork.RollbackTranAsync()`，记录 Error 日志，并重新抛出异常。

### 4.3 与集成事件的协作
- `TransactionBehavior` 在 `IntegrationEventMiddleware` **之前**执行。
- 如果 Handler 中发布了集成事件（`IIntegrationEvent`），这些事件会在事务中间件执行 `next()` 期间被 `IntegrationEventMiddleware` 拦截。
- **重要**：由于 `IntegrationEventMiddleware` 在事务内部执行，如果它仅仅将事件保存到 Outbox（`IIntegrationEventStore`），Outbox 写入也会被包裹在同一个数据库事务中，实现原子性：要么事务提交同时事件持久化，要么回滚全部撤销。
- 若直接发布（无 Outbox），则必须在事务提交后执行，否则可能导致事件已发出而事务回滚造成不一致；`IntegrationEventMiddleware` 的当前实现并未做此优化，建议配合 Outbox 使用。

### 4.4 异常处理
- 事务回滚后，异常被重新抛出，调用方捕获处理。
- 确保 `IUnitOfWork` 的实现（如 SqlSugarUnitWork）正确处理嵌套事务或连接释放。

## 5. IntegrationEventMiddleware

### 5.1 触发条件
- 仅当请求类型实现了 `IIntegrationEvent` 接口时才进入此管道，否则直接 `next()`。
- 这意味着只有 `BaseIntegrationEvent` 或实现了 `IIntegrationEvent` 的事件会被拦截。

### 5.2 行为
1. 尝试从容器获取 `IIntegrationEventStore`。
   - 若存在：调用 `eventStore.SaveAsync(integrationEvent, ct)` 将事件持久化到 Outbox。
   - 若不存在：尝试获取 `IIntegrationPublisher`，若存在则直接调用 `publisher.PublishAsync(...)` 立即发布；若也不存在则抛出 `InvalidOperationException`。
2. **重要**：无论哪种情况，该方法**直接返回 `default!`**，不再继续调用 `next()`。这意味着 MediatR 不会再寻找此事件的 `INotificationHandler`（即集成事件不会触发本地处理器）。这是设计行为：集成事件应完全由出站机制处理，不参与本地多播。

### 5.3 异常传播
- 若 Outbox 保存失败或直接发布失败，异常向上抛出，会被外层 `TransactionBehavior` 捕获并导致事务回滚（如果事务已开启）。
- 抛出异常的日志已记录 Error 级别。

### 5.4 与 TransactionBehavior 的协作
- 如果同时使用了 `[UnitOfWork]` 和 `IIntegrationEvent`：
  - 事务开启 → 进入 `IntegrationEventMiddleware` → 保存事件到 Outbox（使用同一数据库连接） → Handler 执行完毕 → 返回 `TransactionBehavior` → 提交事务。
  - 整个过程中，Outbox 记录作为业务操作的一部分，确保事务成功提交时事件已可靠存储，从而可被后台作业读取并发送。

## 6. 管道整体协作流程图
请求进入
│
├─ 1. LoggingBehavior (记录开始日志)
│
├─ 2. ValidatorBehavior (验证输入，若失败抛异常，中断流程)
│
├─ 3. TransactionBehavior
│ │
│ ├─ 是 Query 或未加 [UnitOfWork] → 直接进入下一步
│ │
│ ├─ 已存在事务 → 直接进入下一步
│ └─ 否则：开启事务
│ │
│ ├─ 4. IntegrationEventMiddleware
│ │ │
│ │ ├─ 请求是 IIntegrationEvent？
│ │ │ ├─ 是：保存到 Outbox / 直接发布 → 返回 default (终止)
│ │ │ └─ 否：调用 next() 进入实际 Handler
│ │ └─ 执行 Handler（或 NotFound）
│ │
│ └─ 返回 TransactionBehavior
│ ├─ 成功 → 提交事务
│ └─ 失败 → 回滚事务，抛出异常
│
└─ 5. LoggingBehavior (记录结束日志，包含耗时)


## 7. 异常处理总结

| 管道 | 异常发生时行为 | 异常是否继续传播 | 是否影响事务 |
|------|----------------|------------------|--------------|
| LoggingBehavior | 仅记录日志（在 `next()` 后执行，异常仍会传播） | 是 | 否（已事后） |
| ValidatorBehavior | 主动抛出 `MasaValidatorException` | 是（停止后续管道） | 否，事务尚未开始 |
| TransactionBehavior | 捕获任何异常，回滚事务，记录错误日志 | 是（重新抛出） | 是，回滚自身事务 |
| IntegrationEventMiddleware | 异常向上抛出（Outbox 保存失败等） | 是 | 若在事务内，会导致事务回滚 |

## 8. 扩展与定制

- **调整管道顺序**：直接在 `AddEventBus` 中修改 `cfg.AddOpenBehavior` 的注册顺序（需修改源码）。
- **添加自定义管道**：创建实现 `IPipelineBehavior<TRequest, TResponse>` 的类，并在调用 `AddMediatR` 时通过 `cfg.AddOpenBehavior` 添加。
- **替换 Outbox 实现**：注册自定义 `IIntegrationEventStore` 服务（Scoped 或 Singleton），`IntegrationEventMiddleware` 会自动使用。