using Lzq.Extensions.AI.AgentSkills;
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
    private readonly AgentSkillProvider _skillProvider;
    private readonly ILogger<AIAgentRunner>? _logger;

    public AIAgentRunner(
        IAIAgentFactory factory,
        ChatHistoryProvider chatHistoryProvider,
        AgentSkillProvider skillProvider,
        ILogger<AIAgentRunner>? logger)
    {
        _factory = factory;
        _chatHistoryProvider = chatHistoryProvider;
        _skillProvider = skillProvider;
        _logger = logger;
    }

    #region 一次性运行（创建 + 运行）

    public async Task<(AgentResponse, string)> RunAsync(AISetting setting, AIAgentModel model, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        var agent = await _factory.CreateAsync(setting, model);
        return await RunAsync(agent, message, sessionDbKey, cancellationToken);
    }

    public async Task<(AgentResponse, string)> RunAsync(IChatClient chatClient, AIAgentModel model, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        var agent = await _factory.CreateAsync(chatClient, model);
        return await RunAsync(agent, message, sessionDbKey, cancellationToken);
    }

    #endregion

    #region 一次性运行（已有 Agent 实例）

    public async Task<(AgentResponse, string)> RunAsync(AIAgent agent, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);
        var result = await agent.RunAsync(message, agentSession, cancellationToken: cancellationToken);
        return (result, sessionDbKey);
    }

    public async Task<(AgentResponse, string)> RunAsync(AIAgent agent, ChatMessage message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);
        var result = await agent.RunAsync(message, agentSession, cancellationToken: cancellationToken);
        return (result, sessionDbKey);
    }

    #endregion

    #region 流式运行（创建 + 运行）

    public async Task<(string, string)> RunStreamingAsync(AISetting setting, AIAgentModel model, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        var agent = await _factory.CreateAsync(setting, model);
        return await RunStreamingAsync(agent, message, callback, sessionDbKey, cancellationToken);
    }

    public async Task<(string, string)> RunStreamingAsync(IChatClient chatClient, AIAgentModel model, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        var agent = await _factory.CreateAsync(chatClient, model);
        return await RunStreamingAsync(agent, message, callback, sessionDbKey, cancellationToken);
    }

    #endregion

    #region 流式运行（已有 Agent 实例）

    public async Task<(string, string)> RunStreamingAsync(AIAgent agent, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken))
    {
        sessionDbKey ??= Guid.NewGuid().ToString();
        var agentSession = await InitializeAgentSessionAsync(agent, sessionDbKey);
        var sb = new StringBuilder();
        var thinkingBuffer = new StringBuilder();
        var textBuffer = new StringBuilder();
        var callIdToToolObject = new Dictionary<string, FunctionCallContent>();

        bool shouldStop = false;
        using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 局部函数
        async Task FlushThinking()
        {
            if (thinkingBuffer.Length == 0) return;
            if (shouldStop) { thinkingBuffer.Clear(); return; }
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
            if (shouldStop) { textBuffer.Clear(); return; }
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

        try
        {
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(message, agentSession, cancellationToken: internalCts.Token))
            {
                if (shouldStop) continue;
                foreach (var content in update.Contents)
                {
                    if (content is TextReasoningContent textReasoningContent)
                    {
                        if (!shouldStop && !string.IsNullOrEmpty(textReasoningContent.Text))
                        {
                            thinkingBuffer.Append(textReasoningContent.Text);
                            if (ShouldFlush(thinkingBuffer)) await FlushThinking();
                        }
                    }
                    else if (content is TextContent textContent)
                    {
                        if (!shouldStop && !string.IsNullOrEmpty(textContent.Text))
                        {
                            textBuffer.Append(textContent.Text);
                            if (ShouldFlush(textBuffer)) await FlushText();
                        }
                    }
                    else if (content is FunctionCallContent functionCall)
                    {
                        await FlushThinking();
                        await FlushText();

                        callIdToToolObject[functionCall.CallId] = functionCall;

                        _logger?.LogDebug($"Agent 请求工具调用: {functionCall.Name}({functionCall.Arguments})");

                        var toolCategory = _skillProvider.GetCategoryByToolName(functionCall.Arguments?["skillName"]?.ToString() ?? "");
                        if (toolCategory == SkillCategory.Visual && functionCall?.Name.Equals("run_skill_script") == true)
                        {
                            await callback(new StreamingEventArgs
                            {
                                EventType = StreamingEventType.EchartsStart,
                                CallId = functionCall.CallId,
                                ToolName = functionCall.Name,
                                ToolArguments = functionCall.Arguments?.ToJson()
                            });
                        }
                        else
                        {
                            await callback(new StreamingEventArgs
                            {
                                EventType = StreamingEventType.ToolCallStart,
                                CallId = functionCall.CallId,
                                ToolName = functionCall.Name,
                                ToolArguments = functionCall.Arguments?.ToJson()
                            });
                        }
                    }
                    else if (content is FunctionResultContent functionResult)
                    {
                        await FlushThinking();
                        await FlushText();

                        var functionCallObj = callIdToToolObject.TryGetValue(functionResult.CallId, out var f) ? f : null;

                        _logger?.LogDebug($"工具调用结果: ToolName={functionCallObj?.Name},CallId={functionResult.CallId}, Result={functionResult.Result}");

                        var toolCategory = _skillProvider.GetCategoryByToolName(functionCallObj?.Arguments?["skillName"]?.ToString() ?? "");
                        if (toolCategory == SkillCategory.Visual && functionCallObj?.Name.Equals("run_skill_script") == true)
                        {
                            await callback(new StreamingEventArgs
                            {
                                EventType = StreamingEventType.EchartsEnd,
                                CallId = functionResult.CallId,
                                ToolName = functionCallObj?.Name,
                                ToolResult = functionResult.Result?.ToString()
                            });
                            bool isExecutionSuccessful = !string.IsNullOrEmpty(functionResult.Result?.ToString())
                                 && !functionResult.Result.ToString().Contains("CONDITION_NOT_MET", StringComparison.OrdinalIgnoreCase);
                            if (!isExecutionSuccessful)//图表生成脚本执行正确就停止后续流程，避免agent继续输出无效内容
                            {
                                shouldStop = true;
                                internalCts.Cancel();
                            }
                        }
                        else
                        {
                            await callback(new StreamingEventArgs
                            {
                                EventType = StreamingEventType.ToolCallEnd,
                                CallId = functionResult.CallId,
                                ToolName = functionCallObj?.Name,
                                ToolResult = functionResult.Result?.ToString()
                            });
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消（可能是 internalCts 触发或外部取消）
        }
        if (!shouldStop)
        {
            await FlushThinking();
            await FlushText();
        }
        else
        {
            thinkingBuffer.Clear();
            textBuffer.Clear();
        }

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