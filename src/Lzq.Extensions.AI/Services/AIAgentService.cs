using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.AI.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System.Text;
using System.Reflection;

namespace Lzq.Extensions.AI.Services
{
    /// <summary>
    /// AI服务
    /// </summary>
    public class AIAgentService(IChatClientService chatClientService, ChatHistoryProvider chatHistoryProvider, AgentSkillProvider agentSkillProvider) : IAIAgentService
    {
        private readonly IChatClientService _chatClientService = chatClientService;
        private readonly ChatHistoryProvider _chatHistoryProvider = chatHistoryProvider;
        private readonly AgentSkillProvider _agentSkillProvider = agentSkillProvider;
        public static string AIRulePrompt = "";

        /// <summary>
        /// 获取代理
        /// </summary>
        /// <returns></returns>
        public IChatClient GetChatClient(string chatClientModel)
        {
            return _chatClientService.GetChatClient(chatClientModel);
        }

        public AIAgent CreateAIAgent(IChatClient chatClient, AIAgentModel aIAgentModel)
        {
            ArgumentNullException.ThrowIfNull(chatClient);
            ArgumentNullException.ThrowIfNull(aIAgentModel);

            var tools = new List<AITool>();

            if (aIAgentModel.SelectedSkills?.Any() == true)
            {
                var allSkillInstances = _agentSkillProvider.GetSkills();

                foreach (var entry in aIAgentModel.SelectedSkills)
                {
                    // 匹配插件实例（增加 dynamic 调用的保护）
                    var skillInstance = allSkillInstances.FirstOrDefault(s =>
                    {
                        try { return (s as dynamic).Frontmatter.Name.Equals(entry.SkillName, StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    });

                    if (skillInstance == null) continue;

                    // 1. 提取所有带有特性的公有方法
                    var methodInfos = skillInstance.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                        .Select(m => new
                        {
                            Method = m,
                            // 检查是否具有框架底层特性
                            Attr = m.GetCustomAttribute<AgentSkillScriptAttribute>(),
                        })
                        .Where(x => x.Attr != null);

                    // 2. 过滤指定方法
                    if (entry.ToolNames?.Any() == true)
                    {
                        // 过滤 Attr.Name
                        methodInfos = methodInfos.Where(x =>
                            !string.IsNullOrEmpty(x.Attr!.Name) &&
                            entry.ToolNames.Contains(x.Attr.Name, StringComparer.OrdinalIgnoreCase));
                    }

                    foreach (var item in methodInfos)
                    {
                        var aiFunc = AIFunctionFactory.Create(item.Method, skillInstance);
                        tools.Add(aiFunc);
                    }
                }
            }

            var options = new ChatClientAgentOptions
            {
                Name = aIAgentModel.Name,
                Description = aIAgentModel.Description,
                ChatOptions = aIAgentModel.ChatOptions ?? new ChatOptions(),
                ChatHistoryProvider = _chatHistoryProvider,
            };

            // 注入工具链
            if (tools.Count > 0)
            {
                options.ChatOptions.Tools = tools.Cast<AITool>().ToList();
            }
            return chatClient.AsAIAgent(options);
        }

        public AIAgent CreateAIAgent(string chatClientModel, AIAgentModel aIAgentModel)
        {
            var chatClient = GetChatClient(chatClientModel);
            return CreateAIAgent(chatClient, aIAgentModel);
        }

        public async Task<(AgentResponse, AgentSession)> RunAsync(AIAgent aiAgent, string message, AgentSession? agentSession = null)
        {
            agentSession ??= await aiAgent.CreateSessionAsync();
            var reslut = await aiAgent.RunAsync(message, agentSession);
            return (reslut, agentSession);
        }

        public async Task<(string, AgentSession)> RunStreamingAsync(AIAgent aiAgent, string message, Func<string,Task> streameCallbackAsync, AgentSession? agentSession = null)
        {
            agentSession ??= await aiAgent.CreateSessionAsync();
            var sb = new StringBuilder();
            await foreach (AgentResponseUpdate update in aiAgent.RunStreamingAsync(message, agentSession))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    await streameCallbackAsync.Invoke(update.Text);
                    sb.Append(update.Text);
                }
            }
            return (sb.ToString(), agentSession);
        }
    }
}
