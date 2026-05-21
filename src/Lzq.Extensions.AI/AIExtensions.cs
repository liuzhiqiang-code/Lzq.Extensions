using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.AI.Factorys;
using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.AI.Provider;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;

namespace Lzq.Extensions.AI
{
    public static class AIExtensions
    {
        public static IServiceCollection AddLzqAI(this IServiceCollection services)
        {
            // 注入消息持久化
            services.AddSingleton<VectorStore>(new InMemoryVectorStore());
            services.AddSingleton<ChatHistoryProvider, VectorChatHistoryProvider>();
            services.AddSingleton<McpToolProvider>();
            services.AddSingleton<AgentSkillProvider>();
            services.AddSingleton<ISkillManager, SkillManager>();
            services.AddSingleton<IChatClientFactory, ChatClientFactory>();
            services.AddSingleton<IAIAgentFactory, AIAgentFactory>();
            services.AddSingleton<IAIAgentRunner, AIAgentRunner>();
            return services;
        }

        public static IServiceCollection AddSqlSugarChatHistoryProvider(this IServiceCollection services)
        {
            services.AddSingleton<ChatHistoryProvider, SqlSugarChatHistoryProvider>();
            return services;
        }
    }
}
