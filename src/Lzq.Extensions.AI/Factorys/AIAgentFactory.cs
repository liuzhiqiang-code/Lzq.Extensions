using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.AI.Provider;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Lzq.Extensions.AI.Factorys;

public class AIAgentFactory : IAIAgentFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ChatHistoryProvider _chatHistoryProvider;
    private readonly AgentSkillProvider _agentSkillProvider;
    private readonly McpToolProvider _mcpToolProvider;

    public AIAgentFactory(
        ILoggerFactory loggerFactory,
        IChatClientFactory chatClientFactory,
        ChatHistoryProvider chatHistoryProvider,
        AgentSkillProvider agentSkillProvider,
        McpToolProvider mcpToolProvider)
    {
        _loggerFactory = loggerFactory;
        _chatClientFactory = chatClientFactory;
        _chatHistoryProvider = chatHistoryProvider;
        _agentSkillProvider = agentSkillProvider;
        _mcpToolProvider = mcpToolProvider;
    }

    public IChatClient GetChatClient(AISetting setting)
    {
        return _chatClientFactory.GetOrCreate(setting);
    }

    public async Task<AIAgent> CreateAsync(IChatClient chatClient, AIAgentModel model)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(model);

        var skillsProvider = _agentSkillProvider.BuildAgentSkillsProviderBySelectedSkills(model.SelectedSkills);
        var aiTools = await _mcpToolProvider.BuildAIToolsAsync(model.SelectedMcpModels);

        var chatOptions = new ChatOptions();
        if (model.ChatOptions != null)
        {
            chatOptions.Instructions = model.ChatOptions.Instructions;
            chatOptions.Temperature = model.ChatOptions.Temperature;
            chatOptions.MaxOutputTokens = model.ChatOptions.MaxOutputTokens;
            chatOptions.TopP = model.ChatOptions.TopP;
            chatOptions.FrequencyPenalty = model.ChatOptions.FrequencyPenalty;
            chatOptions.PresencePenalty = model.ChatOptions.PresencePenalty;
            chatOptions.StopSequences = model.ChatOptions.StopSequences;
            chatOptions.Tools = [.. aiTools];
        }

        var options = new ChatClientAgentOptions
        {
            Name = model.Name,
            Description = model.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = _chatHistoryProvider,
            AIContextProviders = skillsProvider is not null ? [skillsProvider] : null,
        };

        return chatClient.AsBuilder()
            .UseLogging(_loggerFactory)
            .Build()
            .AsAIAgent(options);
    }

    public async Task<AIAgent> CreateAsync(AISetting setting, AIAgentModel model)
    {
        var chatClient = GetChatClient(setting);
        return await CreateAsync(chatClient, model);
    }
}