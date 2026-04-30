using Lzq.Extensions.AI.Interfaces;
using Lzq.Extensions.WebApi.ExternalHttpApis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;
using System.Text.Json;

namespace Lzq.Extensions.WebApi.Services;

/// <summary>
/// 各种并发情况下事务完整性的测试用例
/// </summary>
public class AIService : ServiceBase
{
    private IAIAgentService AIAgentService => GetRequiredService<IAIAgentService>();

    public AIService() : base("/api/v1/test") { }

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "对话补全")]
    [RoutePattern(pattern: "completion", true)]
    public async Task<ApiResult> CompletionAsync()
    {
        var agent = AIAgentService.CreateAIAgent("DeepSeekChat", new AIAgentModel {
            Name = "简单对话助手"
        });

        var (text,agentSession) = await AIAgentService.RunAsync(agent, "我叫Mike，今年25岁。", null);

        // 持久化agentSession
        JsonElement serializedSession = await agent.SerializeSessionAsync(agentSession);
        var agentSessionJson = serializedSession.ToString();


        // 反序列化agentSession
        JsonElement savedElement = JsonDocument.Parse(agentSessionJson).RootElement;
        var agentSession2 = await agent.DeserializeSessionAsync(savedElement);

        (text, agentSession) = await AIAgentService.RunAsync(agent, "我叫什么名字", agentSession2);
        (text, agentSession) = await AIAgentService.RunAsync(agent, "我今年多大", agentSession2);

        return ApiResult.Success();
    }
}