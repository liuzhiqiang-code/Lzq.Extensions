using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.AI.Consts;
using Lzq.Extensions.AI.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using NSwag.Annotations;
using System.Net.Http.Headers;
using System.Text;

namespace Lzq.Extensions.WebApi.Services;

/// <summary>
/// 各种并发情况下事务完整性的测试用例
/// </summary>
public class AIService : ServiceBase
{
    private ILogger<AIService>? Logger => GetService<ILogger<AIService>>();
    private IChatClientService ChatClientService => GetRequiredService<IChatClientService>();
    private IAIAgentService AIAgentService => GetRequiredService<IAIAgentService>();
    private AgentSkillProvider AgentSkillProvider => GetRequiredService<AgentSkillProvider>();
    private IConfiguration Configuration => GetRequiredService<IConfiguration>();

    public AIService() : base("/api/v1/test") { }

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "对话补全")]
    [RoutePattern(pattern: "completion", true)]
    public async Task<ApiResult> CompletionAsync()
    {
        var setting = ChatClientConst.DeepSeek_V4_Flash;
        setting.KeySecret = Configuration.GetSection("AIKeySecret:SiliconFlow").Get<string>() ?? "";
        var agent = AIAgentService.CreateAIAgent(setting, new AIAgentModel
        {
            Name = "简单对话助手"
        });

        var (text, sessionDbKey) = await AIAgentService.RunAsync(agent, "我叫Mike，今年25岁。", null);

        (text, sessionDbKey) = await AIAgentService.RunAsync(agent, "我叫什么名字", sessionDbKey);
        (text, sessionDbKey) = await AIAgentService.RunAsync(agent, "我今年多大", sessionDbKey);

        return ApiResult.Success();
    }

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "对话补全, 带技能")]
    [RoutePattern(pattern: "completionWithSkill", true)]
    public async Task<ApiResult> CompletionWithSkillAsync()
    {
        var result = new List<SkillDemoResult>();

        // 1. 获取所有技能（验证技能是否已注册）
        var allSkills = AgentSkillProvider.GetSkills();
        var skillCount = allSkills.Count();
        Logger?.LogInformation("已注册技能总数: {Count}", skillCount);

        var agent = CreateWorkOrderAgent();

        // ==================== 场景一：查询单个工单进度 ====================
        try
        {
            var updates = new List<AgentResponseUpdate>();
            var textBuilder = new StringBuilder();

            await foreach (var update in AIAgentService.RunStreamingUpdatesAsync(
                agent, "WO-20260510-001 的工单进度怎么样？"))
            {
                updates.Add(update);
                if (!string.IsNullOrEmpty(update.Text))
                    textBuilder.Append(update.Text);
            }

            result.Add(new SkillDemoResult
            {
                Scene = "查询单个工单",
                Question = "WO-20260510-001 的工单进度怎么样？",
                Answer = textBuilder.ToString(),
                UpdateCount = updates.Count,
                Success = true,
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "场景一执行失败");
            result.Add(new SkillDemoResult
            {
                Scene = "查询单个工单",
                Question = "WO-20260510-001 的工单进度怎么样？",
                Answer = $"失败: {ex.Message}",
                Success = false,
            });
        }

        // ==================== 场景二：按状态筛选工单 ====================
        try
        {
            var textBuilder = new StringBuilder();
            int updateCount = 0;

            await foreach (var update in AIAgentService.RunStreamingUpdatesAsync(
                agent, "有哪些待处理的工单？"))
            {
                updateCount++;
                if (!string.IsNullOrEmpty(update.Text))
                    textBuilder.Append(update.Text);
            }

            result.Add(new SkillDemoResult
            {
                Scene = "按状态筛选",
                Question = "有哪些待处理的工单？",
                Answer = textBuilder.ToString(),
                UpdateCount = updateCount,
                Success = true,
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "场景二执行失败");
            result.Add(new SkillDemoResult
            {
                Scene = "按状态筛选",
                Question = "有哪些待处理的工单？",
                Answer = $"失败: {ex.Message}",
                Success = false,
            });
        }

        // ==================== 场景三：按人员筛选工单 ====================
        try
        {
            var textBuilder = new StringBuilder();
            int updateCount = 0;

            await foreach (var update in AIAgentService.RunStreamingUpdatesAsync(
                agent, "张伟负责哪些工单？"))
            {
                updateCount++;
                if (!string.IsNullOrEmpty(update.Text))
                    textBuilder.Append(update.Text);
            }

            result.Add(new SkillDemoResult
            {
                Scene = "按人员筛选",
                Question = "张伟负责哪些工单？",
                Answer = textBuilder.ToString(),
                UpdateCount = updateCount,
                Success = true,
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "场景三执行失败");
            result.Add(new SkillDemoResult
            {
                Scene = "按人员筛选",
                Question = "张伟负责哪些工单？",
                Answer = $"失败: {ex.Message}",
                Success = false,
            });
        }

        // ==================== 场景四：连续对话（带记忆） ====================
        try
        {
            var textBuilder = new StringBuilder();

            // 第一轮：查询工单
            var (answer1, sessionDbKey) = await AIAgentService.RunStreamingAsync(
                agent,
                "帮我查一下 WO-20260510-001 的进度",
                async (chunk) => { textBuilder.Append(chunk); });

            textBuilder.Clear();

            // 第二轮：基于上下文追问（复用 sessionDbKey）
            var (answer2, _) = await AIAgentService.RunStreamingAsync(
                agent,
                "这个工单状态是什么意思？",
                async (chunk) => { textBuilder.Append(chunk); },
                sessionDbKey);  // ← 复用，Agent 会记住上一轮的工单

            result.Add(new SkillDemoResult
            {
                Scene = "连续对话（带记忆）",
                Question = "第一轮：查 WO-20260510-001\n第二轮：追问状态含义",
                Answer = $"第一轮回答：{answer1}\n\n第二轮回答：{answer2}",
                SessionDbKey = sessionDbKey,
                Success = true,
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "场景四执行失败");
            result.Add(new SkillDemoResult
            {
                Scene = "连续对话（带记忆）",
                Question = "查工单 + 追问状态",
                Answer = $"失败: {ex.Message}",
                Success = false,
            });
        }

        // ==================== 返回结果 ====================
        return ApiResult.Success(new
        {
            SkillCount = skillCount,
            Results = result,
            TotalScenes = result.Count,
            SuccessCount = result.Count(r => r.Success),
            FailCount = result.Count(r => !r.Success),
        });
    }

    /// <summary>
    /// 创建工单小助手 Agent（每个场景独立调用）
    /// </summary>
    private AIAgent CreateWorkOrderAgent()
    {
        var setting = ChatClientConst.DeepSeek_V4_Flash;
        setting.KeySecret = Configuration.GetSection("AIKeySecret:SiliconFlow").Get<string>() ?? "";
        var agent = AIAgentService.CreateAIAgent(setting, new AIAgentModel
        {
            Name = $"工单小助手",
            ChatOptions = new ChatOptions
            {
                Instructions = "你是一个专业的工单管理助手。请根据用户的问题，合理使用工单查询技能来提供帮助。",
            },
            SelectedSkills = new List<SkillMethodEntry>
            {
                new SkillMethodEntry
                {
                    SkillName = "work-order-demo",
                }
            },
        });

        Logger?.LogInformation("创建 Agent: {Name}", agent.Name);
        return agent;
    }

    [AllowAnonymous]
    [OpenApiTag("AiChats"), OpenApiOperation("语音转文字", "")]
    [RoutePattern(pattern: "speech-to-text", true)]
    public async Task<ApiResult> SpeechToTextAsync(HttpRequest request)
    {
        // 1. 获取上传文件
        var form = await request.ReadFormAsync();
        var formFile = form.Files["file"];
        if (formFile == null || formFile.Length == 0)
        {
            throw new UserFriendlyException("未找到上传的音频文件");
        }

        // 2. 校验文件（保持原样）
        if (formFile.Length > 25 * 1024 * 1024)
        {
            throw new UserFriendlyException("文件大小超过 25MB 限制");
        }

        // 3. 读取文件为字节数组
        byte[] audioBytes;
        using (var ms = new MemoryStream())
        {
            await formFile.CopyToAsync(ms);
            audioBytes = ms.ToArray();
        }

        using var httpClient = new HttpClient();
        var keySecret = Configuration.GetSection("AIKeySecret:SiliconFlow").Get<string>() ?? "";
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keySecret);

        using var httpForm = new MultipartFormDataContent();
        // 直接使用 IFormFile 的流
        var fileContent = new StreamContent(formFile.OpenReadStream());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(formFile.ContentType);
        httpForm.Add(fileContent, "file", formFile.FileName);
        //httpForm.Add(new StringContent("FunAudioLLM/SenseVoiceSmall"), "model");
        httpForm.Add(new StringContent("FunAudioLLM/SenseVoiceSmall"), "model");

        var response = await httpClient.PostAsync("https://api.siliconflow.cn/v1/audio/transcriptions", httpForm);
        var result = await response.Content.ReadAsStringAsync();
        return ApiResult.Success(result);
    }

    /// <summary>
    /// 技能演示结果 DTO
    /// </summary>
    public class SkillDemoResult
    {
        public string Scene { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public int UpdateCount { get; set; }
        public string? SessionDbKey { get; set; }
        public bool Success { get; set; }
    }
}