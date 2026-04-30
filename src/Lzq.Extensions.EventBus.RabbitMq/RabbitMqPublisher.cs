using Lzq.Extensions.EventBus.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Lzq.Extensions.EventBus.RabbitMq;

/// <summary>
/// 基于 RabbitMQ 的集成事件发布器实现
/// </summary>
public class RabbitMqPublisher : IIntegrationPublisher, IDisposable
{
    private readonly Func<CancellationToken, Task<IConnection>> _createConnectionAsync;

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private IConnection? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _declaredTopics = new();

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger) : this(options, logger, null) { }

    // 内部可测试构造函数
    public RabbitMqPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger,
        Func<CancellationToken, Task<IConnection>>? createConnectionAsync)
    {
        _options = options.Value;
        _logger = logger;
        _createConnectionAsync = createConnectionAsync ?? (async ct =>
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };
            return await factory.CreateConnectionAsync(ct);
        });
    }

    /// <summary>
    /// 发布集成事件
    /// </summary>
    public async Task PublishAsync(IIntegrationEvent @event, CancellationToken ct = default)
    {
        // 默认将类名作为 TopicName
        var topicName = @event.TopicName;

        try
        {
            var connection = await GetConnectionAsync(ct);
            using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            // 声明基础设施
            await DeclareExchangeAndQueueAsync(channel, topicName, ct);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));

            // 配置消息属性 (RabbitMQ 7.x 风格)
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                MessageId = Guid.NewGuid().ToString()
            };

            // 注册异常回调处理 (可选)
            channel.BasicReturnAsync += async (sender, args) =>
            {
                _logger.LogError("[RabbitMq] 消息路由失败返回: {ReplyText}, Exchange: {Exchange}", args.ReplyText, args.Exchange);
            };

            // 发布消息
            await channel.BasicPublishAsync(
                exchange: topicName,
                routingKey: $"{topicName}_routingKey",
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            _logger.LogDebug("[RabbitMq] 集成事件发布成功: {EventName}", topicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RabbitMq] 发布集成事件失败: {EventName}", topicName);
            throw;
        }
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            _logger.LogInformation("[RabbitMq] 正在建立长连接...");
            _connection = await _createConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DeclareExchangeAndQueueAsync(IChannel channel, string topicName, CancellationToken ct)
    {
        if (_declaredTopics.ContainsKey(topicName)) return;

        // 1. 声明死信交换机
        var deadLetterExchange = $"{topicName}_dead_letter_exchange";
        await channel.ExchangeDeclareAsync(deadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: ct);

        // 2. 声明死信队列
        var deadLetterQueue = $"{topicName}_dead_letter_queue";
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        // 3. 绑定死信
        await channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, $"{topicName}_dead_letter_routingKey", cancellationToken: ct);

        // 4. 声明主交换机 (支持延迟插件)
        var exchangeArgs = new Dictionary<string, object?> { ["x-delayed-type"] = "direct" };
        await channel.ExchangeDeclareAsync(
            exchange: topicName,
            type: "x-delayed-message",
            durable: true,
            autoDelete: false,
            arguments: exchangeArgs,
            cancellationToken: ct);

        // 5. 声明主队列
        var queueArgs = new Dictionary<string, object?>
        {
            ["x-max-length"] = 10000,
            ["x-overflow"] = "reject-publish"
        };
        var mainQueue = $"{topicName}_queue";
        await channel.QueueDeclareAsync(mainQueue, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs, cancellationToken: ct);

        // 6. 绑定主队列
        await channel.QueueBindAsync(mainQueue, topicName, $"{topicName}_routingKey", cancellationToken: ct);

        _declaredTopics.TryAdd(topicName, true);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connectionLock.Dispose();
    }
}