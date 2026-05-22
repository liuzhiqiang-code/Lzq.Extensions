using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Lzq.Extensions.AI.Interfaces;

public interface IAIAgentRunner
{
    #region 一次性运行（创建 + 运行）

    Task<(AgentResponse, string)> RunAsync(AISetting setting, AIAgentModel model, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));
    Task<(AgentResponse, string)> RunAsync(IChatClient chatClient, AIAgentModel model, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));

    #endregion

    #region 一次性运行（已有 Agent 实例）

    Task<(AgentResponse, string)> RunAsync(AIAgent agent, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));
    Task<(AgentResponse, string)> RunAsync(AIAgent agent, ChatMessage message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));

    #endregion

    #region 流式运行（创建 + 运行）

    Task<(string, string)> RunStreamingAsync(AISetting setting, AIAgentModel model, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));
    Task<(string, string)> RunStreamingAsync(IChatClient chatClient, AIAgentModel model, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));

    #endregion

    #region 流式运行（已有 Agent 实例）

    Task<(string, string)> RunStreamingAsync(AIAgent agent, string message, Func<StreamingEventArgs, Task> callback, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));

    #endregion

    #region 流式更新（已有 Agent 实例）

    IAsyncEnumerable<AgentResponseUpdate> RunStreamingUpdatesAsync(AIAgent agent, string message, string? sessionDbKey = null, CancellationToken cancellationToken = default(CancellationToken));

    #endregion
}