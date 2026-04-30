using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SqlSugar;
using System.Text.Json;

namespace Lzq.Extensions.AI.Provider;

public sealed class SqlSugarChatHistoryProvider : ChatHistoryProvider
{
    private readonly ProviderSessionState<State> _sessionState;
    private IReadOnlyList<string>? _stateKeys;
    private readonly ISqlSugarClient _sqlSugarClient;

    public SqlSugarChatHistoryProvider(
        ISqlSugarClient sqlSugarClient,
        Func<AgentSession?, State>? stateInitializer = null,
        string? stateKey = null)
    {
        this._sessionState = new ProviderSessionState<State>(
            stateInitializer ?? (_ => new State(Guid.NewGuid().ToString("N"))),
            stateKey ?? this.GetType().Name);
        this._sqlSugarClient = sqlSugarClient ?? throw new ArgumentNullException(nameof(sqlSugarClient));
    }

    public override IReadOnlyList<string> StateKeys => this._stateKeys ??= [this._sessionState.StateKey];

    public string GetSessionDbKey(AgentSession session)
        => this._sessionState.GetOrInitializeState(session).SessionDbKey;

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);

        var records = await this._sqlSugarClient.Queryable<AIChatHistoryEntity>()
            .Where(a=>a.SessionId == state.SessionDbKey)
            .Take(10)
            .OrderByDescending(a=>a.CreationTime)
            .ToListAsync();

        var messages = records.ConvertAll(x => JsonSerializer.Deserialize<ChatMessage>(x.SerializedMessage!)!);
        messages.Reverse();
        return messages;
    }

    protected override async ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);

        var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []).ToList();
        var entities = allNewMessages.Select(x => new AIChatHistoryEntity() 
        {
            Key = state.SessionDbKey + x.MessageId,
            SessionId = state.SessionDbKey,
            Role = x.Role.ToString(),
            SerializedMessage = JsonSerializer.Serialize(x),
            Content = x.Text
        }).ToList();
        if (entities.Any())
            await this._sqlSugarClient.Insertable<AIChatHistoryEntity>(entities).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>
    /// Represents the per-session state stored in the <see cref="AgentSession.StateBag"/>.
    /// </summary>
    public sealed class State
    {
        public State(string sessionDbKey)
        {
            this.SessionDbKey = sessionDbKey ?? throw new ArgumentNullException(nameof(sessionDbKey));
        }

        public string SessionDbKey { get; }
    }
}