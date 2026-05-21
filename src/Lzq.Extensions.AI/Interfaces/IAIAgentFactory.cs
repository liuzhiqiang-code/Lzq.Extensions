using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Lzq.Extensions.AI.Interfaces;

public interface IAIAgentFactory
{
    IChatClient GetChatClient(AISetting setting);
    Task<AIAgent> CreateAsync(IChatClient chatClient, AIAgentModel model);
    Task<AIAgent> CreateAsync(AISetting setting, AIAgentModel model);
}
