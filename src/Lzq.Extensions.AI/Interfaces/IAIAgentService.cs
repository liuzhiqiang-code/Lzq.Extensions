using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Lzq.Extensions.AI.Interfaces
{
    public interface IAIAgentService
    {
        IChatClient GetChatClient(string chatClientModel);

        AIAgent CreateAIAgent(IChatClient chatClient, AIAgentModel aIAgentModel);

        AIAgent CreateAIAgent(string chatClientModel, AIAgentModel aIAgentModel);

        Task<(AgentResponse, AgentSession)> RunAsync(AIAgent aiAgent, string message, AgentSession? agentSession = null);

        Task<(string, AgentSession)> RunStreamingAsync(AIAgent aiAgent, string message, Func<string, Task> streameCallbackAsync, AgentSession? agentSession = null);
    }
}
