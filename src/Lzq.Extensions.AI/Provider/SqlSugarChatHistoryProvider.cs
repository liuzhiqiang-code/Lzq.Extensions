using Lzq.Extensions.AI.AgentSkills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SqlSugar;
using System.Text;
using System.Text.Json;

namespace Lzq.Extensions.AI.Provider;

public sealed class SqlSugarChatHistoryProvider : ChatHistoryProvider
{
    private readonly ProviderSessionState<State> _sessionState;
    private IReadOnlyList<string>? _stateKeys;
    private readonly ISqlSugarClient _sqlSugarClient;
    private readonly AgentSkillProvider _skillProvider;

    public SqlSugarChatHistoryProvider(
        ISqlSugarClient sqlSugarClient,
        AgentSkillProvider skillProvider,
        Func<AgentSession?, State>? stateInitializer = null,
        string? stateKey = null)
    {
        _sessionState = new ProviderSessionState<State>(
            stateInitializer ?? (_ => new State(Guid.NewGuid().ToString("N"))),
            stateKey ?? GetType().Name);
        _sqlSugarClient = sqlSugarClient ?? throw new ArgumentNullException(nameof(sqlSugarClient));
        _skillProvider = skillProvider;
    }

    public override IReadOnlyList<string> StateKeys => _stateKeys ??= [_sessionState.StateKey];

    public string GetSessionDbKey(AgentSession session)
        => _sessionState.GetOrInitializeState(session).SessionDbKey;

    // ==================== 读取历史消息（按轮次截取） ====================
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);

        // 先按时间正序多取一些消息（例如最近 500 条）
        var allRecords = await _sqlSugarClient.Queryable<AIChatHistoryEntity>()
            .Where(a => a.SessionId == state.SessionDbKey)
            .OrderBy(a => a.CreationTime)
            .Take(500)
            .ToListAsync();

        if (allRecords.Count == 0)
            return Enumerable.Empty<ChatMessage>();

        // 从后向前收集最近 10 个不同的轮次
        const int maxTurns = 10;
        var selectedIds = new HashSet<int>();
        var selectedRecords = new List<AIChatHistoryEntity>();

        for (int i = allRecords.Count - 1; i >= 0; i--)
        {
            var rec = allRecords[i];
            int? turnId = rec.TurnId;
            // 忽略无效轮次（兼容旧数据）
            if (turnId == null || selectedIds.Contains(turnId.Value))
                continue;

            selectedIds.Add(turnId.Value);
            if (selectedIds.Count > maxTurns)
                break;
        }

        // 再次遍历，但这次按正序收集所有属于这些轮次的消息
        var resultMessages = allRecords
            .Where(r => r.TurnId.HasValue && selectedIds.Contains(r.TurnId.Value))
            .Select(r => JsonSerializer.Deserialize<ChatMessage>(r.SerializedMessage!)!)
            .ToList();

        return resultMessages;
    }

    // ==================== 存储历史消息 ====================
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);

        var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []).ToList();
        if (allNewMessages.Count == 0) return;

        // 建立本批次消息中的 CallId -> ToolName 映射
        var callIdToFuncCallMap = allNewMessages
            .SelectMany(m => m.Contents ?? [])
            .OfType<FunctionCallContent>()
            .ToDictionary(f => f.CallId, f => f);

        // 确定当前轮次编号
        int currentTurnId;
        if (allNewMessages[0].Role == ChatRole.User)
        {
            var maxTurn = await GetMaxTurnIdFromDb(state.SessionDbKey, cancellationToken);
            currentTurnId = maxTurn + 1;
        }
        else
        {
            var lastTurnId = await GetLastTurnIdFromDb(state.SessionDbKey, cancellationToken);
            currentTurnId = lastTurnId ?? 1;
        }

        // 构造实体
        var entities = allNewMessages.Select(msg =>
        {
            var role = msg.Role.ToString().ToLower();

            return new AIChatHistoryEntity
            {
                Key = $"{state.SessionDbKey}_{msg.MessageId ?? "msg"}_{Guid.NewGuid():N}",
                SessionId = state.SessionDbKey,
                TurnId = currentTurnId,
                Role = role,
                Content = ParseContent(msg, callIdToFuncCallMap),           // 解析 Contents 存到 Content
                SerializedMessage = JsonSerializer.Serialize(msg)
            };
        }).ToList();

        if (entities.Any())
            await _sqlSugarClient.Insertable(entities).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>
    /// 根据 ChatMessage.Contents 类型解析出有意义的文本内容
    /// </summary>
    private List<StreamingEventArgs>? ParseContent(ChatMessage msg, Dictionary<string, FunctionCallContent> callIdToFuncCallMap)
    {
        var parts = new List<StreamingEventArgs>();
        var textBuilder = new StringBuilder();
        var thinkingBuilder = new StringBuilder();

        foreach (var content in msg.Contents ?? [])
        {
            SkillCategory category;
            switch (content)
            {
                case FunctionCallContent funcCall:
                    category = _skillProvider.GetCategoryByToolName(funcCall.Arguments?["skillName"]?.ToString() ?? "");
                    if (category == SkillCategory.Visual && funcCall?.Name.Equals("run_skill_script") == true)
                    {
                        parts.Add(new StreamingEventArgs
                        {
                            EventType = StreamingEventType.EchartsStart,
                            CallId = funcCall.CallId,
                            ToolName = funcCall.Name,
                            ToolArguments = funcCall.Arguments?.ToJson()
                        });
                    }
                    else
                    {
                        parts.Add(new StreamingEventArgs
                        {
                            EventType = StreamingEventType.ToolCallStart,
                            CallId = funcCall.CallId,
                            ToolName = funcCall.Name,
                            ToolArguments = funcCall.Arguments?.ToJson()
                        });
                    }
                    break;

                case FunctionResultContent funcResult:
                    callIdToFuncCallMap.TryGetValue(funcResult.CallId, out var funcCallObj);
                    category = _skillProvider.GetCategoryByToolName(funcCallObj?.Arguments?["skillName"]?.ToString() ?? "");
                    if (category == SkillCategory.Visual && funcCallObj?.Name.Equals("run_skill_script") == true)
                    {
                        parts.Add(new StreamingEventArgs
                        {
                            EventType = StreamingEventType.EchartsEnd,
                            CallId = funcResult.CallId,
                            ToolName = funcCallObj?.Name,
                            ToolResult = funcResult.Result?.ToString()
                        });
                    }
                    else
                    {
                        parts.Add(new StreamingEventArgs
                        {
                            EventType = StreamingEventType.ToolCallEnd,
                            CallId = funcResult.CallId,
                            ToolName = funcCallObj?.Name,
                            ToolResult = funcResult.Result?.ToString()
                        });
                    }
                    break;

                case TextContent text:
                    textBuilder.Append(text.Text);
                    break;
                case TextReasoningContent reasoningContent:
                    thinkingBuilder.Append(reasoningContent.Text);
                    //parts.Add(JsonSerializer.Serialize(new StreamingEventArgs
                    //{
                    //    EventType = StreamingEventType.Thinking,
                    //    Content = reasoningContent.Text
                    //}));
                    break;
            }
        }

        if (textBuilder.Length > 0)
        {
            parts.Add(new StreamingEventArgs
            {
                EventType = StreamingEventType.TextChunk,
                Content = textBuilder.ToString()
            });
        }
        if (thinkingBuilder.Length > 0) 
        {
            parts.Add(new StreamingEventArgs
            {
                EventType = StreamingEventType.Thinking,
                Content = thinkingBuilder.ToString()
            });
        }

        return parts.OrderBy(a => a.EventType).ToList();
    }

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 获取当前会话的最大轮次编号，若没有记录则返回 0
    /// </summary>
    private async Task<int> GetMaxTurnIdFromDb(string sessionDbKey, CancellationToken cancellationToken)
    {
        var last = await _sqlSugarClient.Queryable<AIChatHistoryEntity>()
            .Where(a => a.SessionId == sessionDbKey)
            .OrderBy(a => a.CreationTime, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        return last?.TurnId ?? 0;
    }

    /// <summary>
    /// 获取最近一条记录的轮次编号（用于沿用轮次）
    /// </summary>
    private async Task<int?> GetLastTurnIdFromDb(string sessionDbKey, CancellationToken cancellationToken)
    {
        var last = await _sqlSugarClient.Queryable<AIChatHistoryEntity>()
            .Where(a => a.SessionId == sessionDbKey)
            .OrderBy(a => a.CreationTime, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        return last?.TurnId;
    }

    // ==================== 内部状态 ====================
    public sealed class State
    {
        public string SessionDbKey { get; }
        public State(string sessionDbKey) => SessionDbKey = sessionDbKey ?? throw new ArgumentNullException(nameof(sessionDbKey));
    }
}