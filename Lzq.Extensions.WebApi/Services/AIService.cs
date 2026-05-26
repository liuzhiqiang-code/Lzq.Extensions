using Lzq.Extensions.AI;
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
/// AI 服务测试用例
/// </summary>
public class AIService : ServiceBase
{
    private ILogger<AIService>? Logger => GetService<ILogger<AIService>>();
    private IAIAgentRunner AIAgentRunner => GetRequiredService<IAIAgentRunner>();
    private IAIAgentFactory AIAgentFactory => GetRequiredService<IAIAgentFactory>();
    private AgentSkillProvider AgentSkillProvider => GetRequiredService<AgentSkillProvider>();
    private IConfiguration Configuration => GetRequiredService<IConfiguration>();

    public AIService() : base("/api/v1/test") { }

    #region 基础对话

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "对话补全")]
    [RoutePattern(pattern: "completion", true)]
    public async Task<ApiResult> CompletionAsync()
    {
        var setting = ChatClientConst.DeepSeek_V32;
        setting.KeySecret = Configuration.GetSection("AIKeySecret:SiliconFlow").Get<string>() ?? "";
        var agentModel = new AIAgentModel
        {
            Name = "简单对话助手"
        };

        // 第一轮：自我介绍
        var (response1, sessionDbKey) = await AIAgentRunner.RunAsync(setting, agentModel, "我叫Mike，今年25岁。", null);

        // 第二轮：带记忆追问
        var (response2, _) = await AIAgentRunner.RunAsync(setting, agentModel, "我叫什么名字", sessionDbKey);

        // 第三轮：继续追问
        var (response3, _) = await AIAgentRunner.RunAsync(setting, agentModel, "我今年多大", sessionDbKey);

        return ApiResult.Success(new
        {
            Round1 = response1.Text,
            Round2 = response2.Text,
            Round3 = response3.Text,
            SessionDbKey = sessionDbKey
        });
    }

    #endregion

    #region 带技能的对话

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

        // 2. 创建 Agent（复用同一个实例）
        var agent = await AIAgentFactory.CreateAsync(GetSetting(), CreateWorkOrderAgentModel());

        // ==================== 场景一：查询单个工单进度 ====================
        try
        {
            var textBuilder = new StringBuilder();
            var updates = new List<AgentResponseUpdate>();

            await foreach (var update in AIAgentRunner.RunStreamingUpdatesAsync(
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

            await foreach (var update in AIAgentRunner.RunStreamingUpdatesAsync(
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

            await foreach (var update in AIAgentRunner.RunStreamingUpdatesAsync(
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
            var result1 = await AIAgentRunner.RunStreamingAsync(
                agent,
                "帮我查一下 WO-20260510-001 的进度",
                async (chunk) => { textBuilder.Append(chunk); });

            textBuilder.Clear();

            // 第二轮：基于上下文追问（复用 sessionDbKey）
            var result2 = await AIAgentRunner.RunStreamingAsync(
                agent,
                "这个工单状态是什么意思？",
                async (chunk) => { textBuilder.Append(chunk); },
                result1.SessionDbKey);  // ← 复用，Agent 会记住上一轮的工单

            result.Add(new SkillDemoResult
            {
                Scene = "连续对话（带记忆）",
                Question = "第一轮：查 WO-20260510-001\n第二轮：追问状态含义",
                Answer = $"第一轮回答：{result1.Content}\n\n第二轮回答：{result2.Content}",
                SessionDbKey = result1.SessionDbKey,
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

    #endregion

    #region 流式回调演示

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "流式回调演示（思考/工具调用/文本）")]
    [RoutePattern(pattern: "streamingWithCallback", true)]
    public async Task<ApiResult> StreamingWithCallbackAsync()
    {
        var agent = await AIAgentFactory.CreateAsync(GetSetting(), CreateWorkOrderAgentModel());

        var events = new List<StreamingEvent>();
        var fullText = new StringBuilder();

        var result = await AIAgentRunner.RunStreamingAsync(
            agent,
            "帮我查一下 WO-20260510-001 的工单进度，然后告诉我接下来该怎么做",
            async (args) =>
            {
                switch (args.EventType)
                {
                    case StreamingEventType.Thinking:
                        events.Add(new StreamingEvent
                        {
                            Type = "Thinking",
                            Content = args.Content,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.TextChunk:
                        events.Add(new StreamingEvent
                        {
                            Type = "TextChunk",
                            Content = args.Content,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.ToolCallStart:
                        events.Add(new StreamingEvent
                        {
                            Type = "ToolCallStart",
                            CallId = args.CallId,
                            ToolName = args.ToolName,
                            Content = args.ToolArguments,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.ToolCallEnd:
                        events.Add(new StreamingEvent
                        {
                            Type = "ToolCallEnd",
                            CallId = args.CallId,
                            ToolName = args.ToolName,
                            Content = args.ToolResult,
                            Timestamp = DateTime.Now
                        });
                        break;
                }
            });

        return ApiResult.Success(new
        {
            FullText = result.Content,
            SessionDbKey = result.SessionDbKey,
            TotalEvents = events.Count,
            Events = events
        });
    }

    /// <summary>
    /// 流式事件记录
    /// </summary>
    public class StreamingEvent
    {
        public string Type { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? CallId { get; set; }
        public string? ToolName { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #region 流式回调演示

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "流式回调演示（思考/工具调用/文本/图表）")]
    [RoutePattern(pattern: "streamingWithEcharts", true)]
    public async Task<ApiResult> StreamingWithEchartsAsync()
    {
        var agent = await AIAgentFactory.CreateAsync(GetSetting(), CreateChartAgentModel());

        var events = new List<StreamingEvent>();
        var fullText = new StringBuilder();

        var result = await AIAgentRunner.RunStreamingAsync(
            agent,
            "帮我生成一个0到6月随机数的折线图",
            async (args) =>
            {
                switch (args.EventType)
                {
                    case StreamingEventType.Thinking:
                        events.Add(new StreamingEvent
                        {
                            Type = "Thinking",
                            Content = args.Content,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.TextChunk:
                        events.Add(new StreamingEvent
                        {
                            Type = "TextChunk",
                            Content = args.Content,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.ToolCallStart:
                        events.Add(new StreamingEvent
                        {
                            Type = "ToolCallStart",
                            CallId = args.CallId,
                            ToolName = args.ToolName,
                            Content = args.ToolArguments,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.ToolCallEnd:
                        events.Add(new StreamingEvent
                        {
                            Type = "ToolCallEnd",
                            CallId = args.CallId,
                            ToolName = args.ToolName,
                            Content = args.ToolResult,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.EchartsStart:
                        events.Add(new StreamingEvent
                        {
                            Type = "EchartsStart",
                            CallId = args.CallId,
                            ToolName = args.ToolName,
                            Content = args.ToolArguments,
                            Timestamp = DateTime.Now
                        });
                        break;

                    case StreamingEventType.EchartsEnd:
                        events.Add(new StreamingEvent
                        {
                            Type = "EchartsEnd",
                            CallId = args.CallId,
                            ToolName = args.ToolName,
                            Content = args.ToolResult,
                            Timestamp = DateTime.Now
                        });
                        break;
                }
            });

        return ApiResult.Success(new
        {
            FullText = result.Content,
            SessionDbKey = result.SessionDbKey,
            TotalEvents = events.Count,
            Events = events
        });
    }

    #endregion

    #region MCP 对话

    [AllowAnonymous]
    [OpenApiTag("AI", Description = "对话补全, Mcp")]
    [RoutePattern(pattern: "completionWithMcp", true)]
    public async Task<ApiResult> CompletionWithMcpAsync()
    {
        var result = new List<SkillDemoResult>();
        var agent = await AIAgentFactory.CreateAsync(GetSetting(), CreateMcpAgentModel());
        var question = "请总结与 MCP 工具调用相关的 Azure AI Agent 文档内容?";

        try
        {
            var (response, sessionDbKey) = await AIAgentRunner.RunAsync(agent, question);

            result.Add(new SkillDemoResult
            {
                Scene = "调用Mcp工具",
                Question = question,
                Answer = response.Text,
                Success = true,
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Mcp场景执行失败");
            result.Add(new SkillDemoResult
            {
                Scene = "调用Mcp工具",
                Question = question,
                Answer = $"失败: {ex.Message}",
                Success = false,
            });
        }

        return ApiResult.Success(new
        {
            Results = result,
            TotalScenes = result.Count,
            SuccessCount = result.Count(r => r.Success),
            FailCount = result.Count(r => !r.Success),
        });
    }

    #endregion

    #region 语音转文字

    [AllowAnonymous]
    [OpenApiTag("AiChats"), OpenApiOperation("语音转文字", "")]
    [RoutePattern(pattern: "speech-to-text", true)]
    public async Task<ApiResult> SpeechToTextAsync(HttpRequest request)
    {
        var form = await request.ReadFormAsync();
        var formFile = form.Files["file"];
        if (formFile == null || formFile.Length == 0)
        {
            throw new UserFriendlyException("未找到上传的音频文件");
        }

        if (formFile.Length > 25 * 1024 * 1024)
        {
            throw new UserFriendlyException("文件大小超过 25MB 限制");
        }

        using var httpClient = new HttpClient();
        var keySecret = Configuration.GetSection("AIKeySecret:SiliconFlow").Get<string>() ?? "";
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keySecret);

        using var httpForm = new MultipartFormDataContent();
        var fileContent = new StreamContent(formFile.OpenReadStream());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(formFile.ContentType);
        httpForm.Add(fileContent, "file", formFile.FileName);
        httpForm.Add(new StringContent("FunAudioLLM/SenseVoiceSmall"), "model");

        var response = await httpClient.PostAsync("https://api.siliconflow.cn/v1/audio/transcriptions", httpForm);
        var result = await response.Content.ReadAsStringAsync();
        return ApiResult.Success(result);
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 获取通用 Setting
    /// </summary>
    private AISetting GetSetting()
    {
        return new AISetting
        {
            ConfigId = ChatClientConst.MiniMaxM25.ConfigId,
            Url = ChatClientConst.MiniMaxM25.Url,
            KeySecret = Configuration.GetSection("AIKeySecret:SiliconFlow").Get<string>() ?? "",
            Model = ChatClientConst.MiniMaxM25.Model
        };
    }

    /// <summary>
    /// 创建工单 Agent 配置
    /// </summary>
    private AIAgentModel CreateWorkOrderAgentModel()
    {
        return new AIAgentModel
        {
            Name = "工单小助手",
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
        };
    }

    /// <summary>
    /// 创建图表 Agent 配置
    /// </summary>
    private AIAgentModel CreateChartAgentModel()
    {
        return new AIAgentModel
        {
            Name = "图表小助手",
            ChatOptions = new ChatOptions
            {
                Instructions = "你是一个专业助手。当用户要求生成图表时，请优先使用 'generic-data-analyzer' 技能。调用后，请等待工具结果，并将数据展示为Echats图表配置json。",
            },
            SelectedSkills = new List<SkillMethodEntry>
            {
                new SkillMethodEntry
                {
                    SkillName = "generic-data-analyzer",
                }
            },
        };
    }

    /// <summary>
    /// 创建 MCP Agent 配置
    /// </summary>
    private AIAgentModel CreateMcpAgentModel()
    {
        return new AIAgentModel
        {
            Name = "Mcp小助手",
            ChatOptions = new ChatOptions
            {
                Instructions = "You answer questions by searching the Microsoft Learn content only.",
            },
            SelectedMcpModels = new List<McpModel>
            {
                new McpModel
                {
                    Name = "microsoft_learn",
                    Url = "https://learn.microsoft.com/api/mcp",
                    AllowedTools = new List<string> { "microsoft_docs_search" }
                }
            },
        };
    }

    #endregion

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