using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.AI.Provider;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Lzq.Extensions.AI.Services
{
    /// <summary>
    /// AI服务
    /// </summary>
    public class AIAgentService : IAIAgentService
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IChatClientService _chatClientService;
        private readonly ChatHistoryProvider _chatHistoryProvider;
        private readonly AgentSkillProvider _agentSkillProvider;
        private readonly ILogger<AIAgentService> _logger;

        public AIAgentService(
            ILoggerFactory loggerFactory,
            IChatClientService chatClientService,
            ChatHistoryProvider chatHistoryProvider,
            AgentSkillProvider agentSkillProvider,
            ILogger<AIAgentService> logger)
        {
            _loggerFactory = loggerFactory;
            _chatClientService = chatClientService;
            _chatHistoryProvider = chatHistoryProvider;
            _agentSkillProvider = agentSkillProvider;
            _logger = logger;
        }

        /// <summary>
        /// 获取代理
        /// </summary>
        /// <returns></returns>
        public IChatClient GetChatClient(AISetting setting)
        {
            return _chatClientService.GetChatClient(setting);
        }

        public AIAgent CreateAIAgent(IChatClient chatClient, AIAgentModel model)
        {
            ArgumentNullException.ThrowIfNull(chatClient);
            ArgumentNullException.ThrowIfNull(model);

            var skillsProvider = _agentSkillProvider.BuildAgentSkillsProviderBySelectedSkills(model.SelectedSkills);

            // 深拷贝 ChatOptions 避免引用污染
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
                .UseLogging(_loggerFactory) // 添加日志中间件
                .Build() // 构建Agent
                .AsAIAgent(options);
        }

        public AIAgent CreateAIAgent(AISetting setting, AIAgentModel aIAgentModel)
        {
            var chatClient = GetChatClient(setting);
            return CreateAIAgent(chatClient, aIAgentModel);
        }

        public async Task<(AgentResponse, string)> RunAsync(AIAgent aiAgent, string message, string? sessionDbKey = null)
        {
            sessionDbKey ??= Guid.NewGuid().ToString();
            var agentSession = await InitializeAgentSessionAsync(aiAgent, sessionDbKey);
            var reslut = await aiAgent.RunAsync(message, agentSession);
            return (reslut, sessionDbKey);
        }

        public async Task<(AgentResponse, string)> RunAsync(AIAgent aiAgent, ChatMessage message, string? sessionDbKey = null)
        {
            sessionDbKey ??= Guid.NewGuid().ToString();
            var agentSession = await InitializeAgentSessionAsync(aiAgent, sessionDbKey);
            var reslut = await aiAgent.RunAsync(message, agentSession);
            return (reslut, sessionDbKey);
        }

        public async Task<(string, string)> RunStreamingAsync(AIAgent aiAgent, string message, Func<string,Task> streameCallbackAsync, string? sessionDbKey = null)
        {
            sessionDbKey ??= Guid.NewGuid().ToString();
            var agentSession = await InitializeAgentSessionAsync(aiAgent, sessionDbKey);
            var sb = new StringBuilder();
            await foreach (AgentResponseUpdate update in aiAgent.RunStreamingAsync(message, agentSession))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    sb.Append(update.Text);
                    await streameCallbackAsync.Invoke(update.Text);
                }
                // 2. 工具调用请求 / 工具结果（存在于 Contents 中）
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent functionCall)
                    {
                        _logger.LogDebug("Agent 请求工具调用: {ToolName}({Arguments})",
                            functionCall.Name, functionCall.Arguments);
                    }
                    else if (content is FunctionResultContent functionResult)
                    {
                        _logger.LogDebug("工具调用结果: CallId={CallId}, Result={Result}",
                        functionResult.CallId, functionResult.Result);
                    }
                }
            }
            return (sb.ToString(), sessionDbKey);
        }

        public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingUpdatesAsync(
            AIAgent aiAgent,
            string message,
            string? sessionDbKey = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            //var chatMessage = new Microsoft.Extensions.AI.ChatMessage
            //{
            //    Role = ChatRole.User,
            //    Contents = new List<AIContent> { new DataContent( new Uri(""),"" )  }
            //};
            //var json = chatMessage.ToJson();

            sessionDbKey ??= Guid.NewGuid().ToString();
            var agentSession = await InitializeAgentSessionAsync(aiAgent, sessionDbKey);
            await foreach (AgentResponseUpdate update in aiAgent.RunStreamingAsync(message, agentSession, null, cancellationToken))
            {
                yield return update;
            }
        }

        private async Task<AgentSession> InitializeAgentSessionAsync(AIAgent aiAgent, string sessionDbKey)
        {
            //// 持久化agentSession
            //JsonElement serializedSession = await agent.SerializeSessionAsync(agentSession);
            //var agentSessionJson = serializedSession.ToString();


            //// 反序列化agentSession
            //JsonElement savedElement = JsonDocument.Parse(agentSessionJson).RootElement;
            //var agentSession2 = await agent.DeserializeSessionAsync(savedElement);

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
            return await aiAgent.DeserializeSessionAsync(savedElement);
        }
    }
}
