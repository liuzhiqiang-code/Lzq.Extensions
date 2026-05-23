using Lzq.Core;
using Lzq.Core.Modules;
using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.AI.Factorys;
using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.AI.Provider;
using Lzq.Extensions.SqlSugar;
using Masa.BuildingBlocks.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;

namespace Lzq.Extensions.AI;

[DependsOn(typeof(CoreModule), typeof(SqlSugarModule))]
public class AIModule : BaseModule
{
    public override void Configure(ModuleConfigureContext context)
    {
        var currentAssembly = typeof(AIModule).Assembly;
        MasaApp.TryAddAssemblies(currentAssembly);
    }

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        // 注入消息持久化
        services.AddSingleton<VectorStore>(new InMemoryVectorStore());
        // services.AddSingleton<ChatHistoryProvider, VectorChatHistoryProvider>();
        services.AddSingleton<ChatHistoryProvider, SqlSugarChatHistoryProvider>();
        services.AddSingleton<McpToolProvider>();
        services.AddSingleton<AgentSkillProvider>();
        services.AddSingleton<ISkillManager, SkillManager>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IAIAgentFactory, AIAgentFactory>();
        services.AddSingleton<IAIAgentRunner, AIAgentRunner>();
    }
}
