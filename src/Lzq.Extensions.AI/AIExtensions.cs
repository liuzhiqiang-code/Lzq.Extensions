using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.AI.Provider;
using Lzq.Extensions.AI.Services;
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
            services.AddOptions<List<AISetting>>().BindConfiguration("AISettings")
                .Validate(settings => settings.Count > 0, "AISettings 列表不能为空")
                .ValidateOnStart();

            // 注入消息持久化
            services.AddSingleton<VectorStore>(new InMemoryVectorStore());
            services.AddSingleton<ChatHistoryProvider, VectorChatHistoryProvider>();
            services.AddSingleton<IChatClientService, ChatClientService>();
            services.AddTransient<IAIAgentService, AIAgentService>();
            return services;
        }

        public static IServiceCollection AddSqlSugarChatHistoryProvider(this IServiceCollection services)
        {
            services.AddSingleton<ChatHistoryProvider, SqlSugarChatHistoryProvider>();
            return services;
        }
    }
}
