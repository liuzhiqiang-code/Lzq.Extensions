using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using System.Text.Json;

namespace Lzq.Extensions.AI.Provider;

internal sealed class VectorChatHistoryProvider : ChatHistoryProvider
{
    private readonly ProviderSessionState<State> _sessionState;
    private IReadOnlyList<string>? _stateKeys;
    private readonly VectorStore _vectorStore;

    public VectorChatHistoryProvider(
        VectorStore vectorStore,
        Func<AgentSession?, State>? stateInitializer = null,
        string? stateKey = null)
    {
        _sessionState = new ProviderSessionState<State>(
            stateInitializer ?? (_ => new State(Guid.NewGuid().ToString("N"))),
            stateKey ?? GetType().Name);
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
    }

    public override IReadOnlyList<string> StateKeys => _stateKeys ??= [_sessionState.StateKey];

    public string GetSessionDbKey(AgentSession session)
        => _sessionState.GetOrInitializeState(session).SessionDbKey;

    // ==================== 读取历史消息 ====================
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);
        var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        // 按时间倒序获取最近的消息，多取一些再裁剪
        var records = await collection
            .GetAsync(
                x => x.SessionId == state.SessionDbKey,
                500,
                new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                cancellationToken)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
            return Enumerable.Empty<ChatMessage>();

        // 记录已处理的轮次编号，从最新消息向前收集完整轮次
        var selectedRecords = new List<ChatHistoryItem>();
        var seenTurns = new HashSet<int>();
        int turnCount = 0;
        const int maxTurns = 10;

        foreach (var record in records)
        {
            int? turnId = record.TurnId;
            // 忽略无效轮次（兼容旧数据）
            if (turnId == null)
                continue;

            if (!seenTurns.Contains(turnId.Value))
            {
                seenTurns.Add(turnId.Value);
                turnCount++;
                if (turnCount > maxTurns)
                    break;
            }

            selectedRecords.Add(record);
        }

        // 反序列化并反转，得到时间正序的消息列表
        var messages = selectedRecords
            .Select(x => JsonSerializer.Deserialize<ChatMessage>(x.SerializedMessage!)!)
            .Reverse()
            .ToList();

        return messages;
    }

    // ==================== 存储历史消息 ====================
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);
        var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []).ToList();
        if (allNewMessages.Count == 0) return;

        // 确定当前轮次编号
        int currentTurnId;
        if (allNewMessages[0].Role == ChatRole.User)
        {
            // 新轮次：取当前最大轮次编号 + 1
            var maxTurn = await GetMaxTurnIdFromStore(state.SessionDbKey, collection, cancellationToken);
            currentTurnId = maxTurn + 1;
        }
        else
        {
            // 后续消息（assistant/tool）沿用上一轮编号
            var lastTurnId = await GetLastTurnIdFromStore(state.SessionDbKey, collection, cancellationToken);
            currentTurnId = lastTurnId ?? 1; // 兜底从1开始
        }

        // 构造存储项，Key 保证绝对唯一
        var items = allNewMessages.Select(msg => new ChatHistoryItem
        {
            Key = $"{state.SessionDbKey}_{msg.MessageId ?? "msg"}_{Guid.NewGuid():N}",
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = state.SessionDbKey,
            TurnId = currentTurnId,
            SerializedMessage = JsonSerializer.Serialize(msg),
            MessageText = msg.Text
        });

        await collection.UpsertAsync(items, cancellationToken);
    }

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 获取当前会话的最大轮次编号，若没有记录则返回 0
    /// </summary>
    private async Task<int> GetMaxTurnIdFromStore(
        string sessionDbKey,
        VectorStoreCollection<string, ChatHistoryItem> collection,
        CancellationToken cancellationToken)
    {
        var lastRecord = await collection
            .GetAsync(
                x => x.SessionId == sessionDbKey,
                1,
                new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                cancellationToken)
            .ToListAsync(cancellationToken);

        return lastRecord.FirstOrDefault()?.TurnId ?? 0;
    }

    /// <summary>
    /// 获取最近一条记录的轮次编号（用于沿用轮次）
    /// </summary>
    private async Task<int?> GetLastTurnIdFromStore(
        string sessionDbKey,
        VectorStoreCollection<string, ChatHistoryItem> collection,
        CancellationToken cancellationToken)
    {
        var lastRecord = await collection
            .GetAsync(
                x => x.SessionId == sessionDbKey,
                1,
                new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                cancellationToken)
            .ToListAsync(cancellationToken);

        return lastRecord.FirstOrDefault()?.TurnId;
    }

    // ==================== 内部类型 ====================

    public sealed class State
    {
        public string SessionDbKey { get; }
        public State(string sessionDbKey) => SessionDbKey = sessionDbKey ?? throw new ArgumentNullException(nameof(sessionDbKey));
    }

    private sealed class ChatHistoryItem
    {
        [VectorStoreKey]
        public string? Key { get; set; }

        [VectorStoreData]
        public string? SessionId { get; set; }

        [VectorStoreData]
        public int? TurnId { get; set; }

        [VectorStoreData]
        public DateTimeOffset? Timestamp { get; set; }

        [VectorStoreData]
        public string? SerializedMessage { get; set; }

        [VectorStoreData]
        public string? MessageText { get; set; }
    }
}