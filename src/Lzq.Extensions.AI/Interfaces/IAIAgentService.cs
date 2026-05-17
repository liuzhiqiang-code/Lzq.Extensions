using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Lzq.Extensions.AI.Interfaces
{
    public interface IAIAgentService
    {
        IChatClient GetChatClient(AISetting setting);

        AIAgent CreateAIAgent(IChatClient chatClient, AIAgentModel aIAgentModel);

        AIAgent CreateAIAgent(AISetting setting, AIAgentModel aIAgentModel);

        Task<(AgentResponse, string)> RunAsync(AIAgent aiAgent, string message, string? sessionDbKey = null);

        Task<(AgentResponse, string)> RunAsync(AIAgent aiAgent, ChatMessage message, string? sessionDbKey = null);

        Task<(string, string)> RunStreamingAsync(AIAgent aiAgent, string message, Func<string, Task> streameCallbackAsync, string? sessionDbKey = null);

        IAsyncEnumerable<AgentResponseUpdate> RunStreamingUpdatesAsync(
            AIAgent agent,
            string message,
            string? sessionDbKey = null,
            CancellationToken cancellationToken = default);
    }
}
