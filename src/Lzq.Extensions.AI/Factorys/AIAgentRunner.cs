using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.AI.Provider;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lzq.Extensions.AI.Factorys;

public class AIAgentRunner : IAIAgentRunner
{
    private readonly IAIAgentFactory _factory;
    private readonly ChatHistoryProvider _chatHistoryProvider;
    private readonly ILogger<AIAgentRunner>? _logger;

    public AIAgentRunner(
        IAIAgentFactory factory,
        ChatHistoryProvider chatHistoryProvider,
        ILogger<AIAgentRunner>? logger)
    {
        _factory = factory;
        _chatHistoryProvider = chatHistoryProvider;
        _logger = logger;
    }

    #region 一次性运行（创建 + 运行）

    public async Task<(AgentResponse, string)> RunAsync(AISetting setting, AIAgentModel model, string message, string? sessionDbKey = null)
    {
        var agent = await _factory.CreateAsync(setting, model);
        return await RunAsync(agent, message, sessionDbKey);
    }

    public async Task<(AgentResponse, string)> RunAsync(IChatClient chatClient, AIAgentModel model, string message, string? sessionDbKey = null)
    {
        var agent = await _factory.CreateAsync(chatClient, model);
        return await RunAsync(agent, message, sessionDbKey);
    }

    #endregion

    #region 一次性运行（已有 Agent 实例）

    public async Task<(AgentResponse, string)> RunAsync(AIAgent agent, string message, string? sessionDbKey = null)
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);
        var result = await agent.RunAsync(message, agentSession);
        return (result, sessionDbKey);
    }

    public async Task<(AgentResponse, string)> RunAsync(AIAgent agent, ChatMessage message, string? sessionDbKey = null)
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);
        var result = await agent.RunAsync(message, agentSession);
        return (result, sessionDbKey);
    }

    #endregion

    #region 流式运行（创建 + 运行）

    public async Task<(string, string)> RunStreamingAsync(AISetting setting, AIAgentModel model, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null)
    {
        var agent = await _factory.CreateAsync(setting, model);
        return await RunStreamingAsync(agent, message, callback, sessionDbKey);
    }

    public async Task<(string, string)> RunStreamingAsync(IChatClient chatClient, AIAgentModel model, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null)
    {
        var agent = await _factory.CreateAsync(chatClient, model);
        return await RunStreamingAsync(agent, message, callback, sessionDbKey);
    }

    #endregion

    #region 流式运行（已有 Agent 实例）

    public async Task<(string, string)> RunStreamingAsync(AIAgent agent, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null)
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);
        var sb = new StringBuilder();
        var thinkingBuffer = new StringBuilder();
        var textBuffer = new StringBuilder();
        var callIdToToolName = new Dictionary<string, string>();

        // 局部函数
        async Task FlushThinking()
        {
            if (thinkingBuffer.Length == 0) return;
            await callback(new StreamingEventArgs
            {
                EventType = StreamingEventType.Thinking,
                Content = thinkingBuffer.ToString()
            });
            thinkingBuffer.Clear();
        }

        async Task FlushText()
        {
            if (textBuffer.Length == 0) return;
            var text = textBuffer.ToString();
            sb.Append(text);
            await callback(new StreamingEventArgs
            {
                EventType = StreamingEventType.TextChunk,
                Content = text
            });
            textBuffer.Clear();
        }

        static bool ShouldFlush(StringBuilder buffer)
        {
            var s = buffer.ToString();
            return s.EndsWith("。") || s.EndsWith("\n") || s.EndsWith("！") || s.EndsWith("？") || s.Length >= 100;
        }

        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(message, agentSession))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextReasoningContent textReasoningContent)
                {
                    if (!string.IsNullOrEmpty(textReasoningContent.Text))
                    {
                        thinkingBuffer.Append(textReasoningContent.Text);
                        if (ShouldFlush(thinkingBuffer)) await FlushThinking();
                    }
                }
                else if (content is TextContent textContent)
                {
                    if (!string.IsNullOrEmpty(textContent.Text))
                    {
                        textBuffer.Append(textContent.Text);
                        if (ShouldFlush(textBuffer)) await FlushText();
                    }
                }
                else if (content is FunctionCallContent functionCall)
                {
                    await FlushThinking();
                    await FlushText();

                    callIdToToolName[functionCall.CallId] = functionCall.Name;

                    _logger?.LogDebug($"Agent 请求工具调用: {functionCall.Name}({functionCall.Arguments})");

                    await callback(new StreamingEventArgs
                    {
                        EventType = StreamingEventType.ToolCallStart,
                        CallId = functionCall.CallId,
                        ToolName = functionCall.Name,
                        ToolArguments = functionCall.Arguments?.ToJson()
                    });
                }
                else if (content is FunctionResultContent functionResult)
                {
                    await FlushThinking();
                    await FlushText();

                    var toolName = callIdToToolName.TryGetValue(functionResult.CallId, out var name)
                    ? name
                    : functionResult.CallId;

                    _logger?.LogDebug($"工具调用结果: ToolName={toolName},CallId={functionResult.CallId}, Result={functionResult.Result}");

                    await callback(new StreamingEventArgs
                    {
                        EventType = StreamingEventType.ToolCallEnd,
                        CallId = functionResult.CallId,
                        ToolName = toolName,
                        ToolResult = functionResult.Result?.ToString()
                    });
                }
            }
        }

        await FlushThinking();
        await FlushText();

        return (sb.ToString(), sessionDbKey);
    }

    #endregion

    #region 流式更新（已有 Agent 实例）

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingUpdatesAsync(
        AIAgent agent,
        string message,
        string? sessionDbKey = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);

        await foreach (var update in agent.RunStreamingAsync(message, agentSession, null, cancellationToken))
        {
            yield return update;
        }
    }

    #endregion

    #region 私有方法

    private async Task<AgentSession> InitializeAgentSessionAsync(AIAgent agent, string sessionDbKey)
    {
        var root = new JsonObject();
        var stateBagObj = new JsonObject();

        if (_chatHistoryProvider is SqlSugarChatHistoryProvider)
        {
            stateBagObj["SqlSugarChatHistoryProvider"] = new JsonObject
            {
                ["sessionDbKey"] = sessionDbKey
            };
        }
        else if (_chatHistoryProvider is VectorChatHistoryProvider)
        {
            stateBagObj["VectorChatHistoryProvider"] = new JsonObject
            {
                ["sessionDbKey"] = sessionDbKey
            };
        }

        root["stateBag"] = stateBagObj;
        JsonElement savedElement = JsonDocument.Parse(root.ToJsonString()).RootElement;
        return await agent.DeserializeSessionAsync(savedElement);
    }

    #endregion
}