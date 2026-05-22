## Lzq.Extensions.AI — AgentSkill 技能引擎

基于 **Microsoft.Agents.AI** 构建的 AI Agent 扩展库，提供多模型对话、技能调用、MCP 工具集成、流式输出与会话持久化。

---

## 快速开始

```csharp
// DI 注册
services.AddLzqAI();
// 使用 SQL 持久化历史（可选）
services.AddLzqAI().AddSqlSugarChatHistoryProvider();

// 创建 Agent 并执行
var (response, sessionKey) = await runner.RunAsync(
    new AISetting
    {
        ConfigId = "default",
        Url = "https://api.siliconflow.cn/v1/",
        KeySecret = "sk-xxx",
        Model = "deepseek-v4-flash"
    },
    new AIAgentModel
    {
        Name = "助手",
        Description = "通用 AI 助手",
        SelectedSkills = { new SkillMethodEntry { SkillName = "time-master" } }
    },
    "北京时间现在几点？");
```

---

## 核心架构

```
┌─────────────────────────────────────────────────────┐
│                    DI Container                      │
├─────────────────────────────────────────────────────┤
│  IChatClientFactory → ChatClientFactory             │
│    ├── OpenAI 兼容 API (OpenAIClient)               │
│    └── Ollama (OllamaApiClient)                     │
│                                                     │
│  IAIAgentFactory → AIAgentFactory                   │
│    ├── 绑定技能 (AgentSkillProvider)                │
│    ├── 绑定 MCP 工具 (McpToolProvider)              │
│    └── 日志中间件                                   │
│                                                     │
│  IAIAgentRunner → AIAgentRunner                     │
│    ├── 同步对话 (RunAsync)                          │
│    ├── 流式对话 (RunStreamingAsync)                 │
│    └── 原始流 (RunStreamingUpdatesAsync)            │
│                                                     │
│  AgentSkillProvider                                  │
│    ├── 内部 C# 技能 (AgentSkills/*.dll)             │
│    ├── 外部 MD 技能 (ExternalSkills/**/SKILL.md)    │
│    └── 文件热重载 (FileSystemWatcher)               │
│                                                     │
│  ChatHistoryProvider                                 │
│    ├── VectorChatHistoryProvider (默认)              │
│    └── SqlSugarChatHistoryProvider (可选)            │
│                                                     │
│  McpToolProvider                                     │
│    ├── HTTP (HostedMcpServerTool)                   │
│    └── Stdio (本地进程)                             │
└─────────────────────────────────────────────────────┘
```

---

## 1. 多模型支持

| 后端 | 识别方式 | 说明 |
|------|---------|------|
| OpenAI 兼容 | `ConfigId` 非 Ollama 前缀 | 适配任意 OpenAI 兼容 API |
| Ollama | `ConfigId` 以 `"Ollama"` 开头 | 本地模型推理 |

**内置预设：**

```csharp
ChatClientConst.DeepSeek_V4_Flash  // SiliconFlow + DeepSeek-V4-Flash
ChatClientConst.DeepSeek_V32       // SiliconFlow + DeepSeek-V3.2
ChatClientConst.MiniMaxM25         // SiliconFlow + MiniMax-M2.5
```

---

## 2. AgentSkill 技能系统

### 2.1 内部 C# 技能（强类型 · DI 注入）

```csharp
[GeneralSkill]  // 每次对话自动加载
public class TimeSkill : LzqAgentSkillBase<TimeSkill>
{
    protected override string SkillName => "time-master";
    protected override string SkillDescription => "时间查询与时区转换";

    [AgentSkillScript("GetCurrentTime")]
    [Description("获取指定时区的当前时间")]
    public async Task<string> GetCurrentTime(string timezoneId = "Asia/Shanghai")
    {
        var time = TimeZoneInfo.ConvertTime(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(timezoneId));
        return $"{timezoneId} 当前时间: {time:yyyy-MM-dd HH:mm:ss}";
    }
}
```

**技能分类：**

| 分类 | 说明 | 示例 |
|------|------|------|
| `Core` | 核心通用，自动加载 | TimeSkill |
| `Visual` | 可视化图表 | GenericDataAnalysisSkill |
| `Network` | 网络请求 | WebSearchSkill |
| `Office` | 业务办公 | QueryWorkOrderSkill |

**内置演示技能：**

- **TimeSkill** (`Core`) — 时区时间查询/转换/计算
- **WebSearchSkill** (`Network`) — 多引擎搜索 + URL 内容抓取
- **QueryWorkOrderSkill** (`Office`) — 工单进度/列表查询
- **GenericDataAnalysisSkill** (`Visual`) — ECharts JSON 图表生成

### 2.2 外部 Markdown 技能（零代码 · 热加载）

```
ExternalSkills/
├── lzq-dev-handbook/
│   ├── SKILL.md
│   ├── specs/
│   └── extensions/
├── mes-operation-handbook/
│   ├── SKILL.md
│   └── operations/
└── vben-frontend-dev/
    ├── SKILL.md
    ├── patterns/
    └── components/
```

- `FileSystemWatcher` 监听，300ms 防抖热重载
- `NoOpScriptRunner` 禁用脚本执行，安全可控

### 2.3 技能热重载与管理

```csharp
// 枚举技能
var skills = skillManager.GetSkills();

// 手动执行技能工具
var result = await skillManager.ExecuteAsync("time-master", "GetCurrentTime", args);

// 上传 DLL 插件
await skillManager.UploadPluginAsync("my-skill.dll", stream);

// 上传 ZIP 外部技能
await skillManager.UploadExternalSkillZipAsync("docs.zip", stream);
```

---

## 3. MCP 工具集成

支持两种传输模式：

| 模式 | 说明 |
|------|------|
| **HTTP** | 配置 Url/Headers/AllowedTools，远程 MCP 服务器 |
| **Stdio** | 本地进程启动，command + arguments |

```csharp
new McpModel
{
    McpName = "database-mcp",
    McpType = McpTypeEnum.Stdio,
    Url = "npx",
    Args = "-y @modelcontextprotocol/server-postgres",
    AllowedTools = ["query", "list-tables"]
}
```

---

## 4. 流式对话

```csharp
var (text, sessionKey) = await runner.RunStreamingAsync(
    setting, model, "分析销售数据并生成图表",
    async args =>
    {
        switch (args.EventType)
        {
            case StreamingEventType.Thinking:
                Console.WriteLine($"思考中: {args.Content}");
                break;
            case StreamingEventType.TextChunk:
                Console.Write(args.Content);
                break;
            case StreamingEventType.ToolCallStart:
                Console.WriteLine($"调用工具: {args.ToolName}");
                break;
            case StreamingEventType.ToolCallEnd:
                Console.WriteLine($"工具返回: {args.ToolResult}");
                break;
            case StreamingEventType.EchartsStart:
                Console.WriteLine("生成图表...");
                break;
        }
    });
```

---

## 5. 会话历史

| 提供者 | 说明 | 注册方式 |
|--------|------|---------|
| `VectorChatHistoryProvider` | 默认，SemanticKernel InMemoryVectorStore | `AddLzqAI()` |
| `SqlSugarChatHistoryProvider` | SQL 持久化，表 `ai_chat_history` | `.AddSqlSugarChatHistoryProvider()` |

- 基于 turn 追踪（用户消息开启新 turn）
- 滑动窗口保留最近 10 轮会话

---

## 6. 配置模型

### AISetting

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ConfigId` | string | — | 配置标识（Ollama 前缀触发 Ollama 后端） |
| `Url` | string | `https://dashscope.aliyuncs.com/compatible-mode/v1/` | API 地址 |
| `KeySecret` | string | — | API 密钥 |
| `Model` | string | `deepseek-v3` | 模型名称 |

### AIAgentModel

| 字段 | 类型 | 说明 |
|------|------|------|
| `Name` | string | Agent 名称 |
| `Description` | string | Agent 描述 |
| `ChatOptions` | ChatOptions | 温度/Token 等参数 |
| `SelectedSkills` | List\<SkillMethodEntry\> | 启用的技能列表 |
| `SelectedMcpModels` | List\<McpModel\> | 启用的 MCP 工具列表 |

---

## 7. 关键接口

| 接口 | 实现 | 生命周期 | 作用 |
|------|------|---------|------|
| `IChatClientFactory` | `ChatClientFactory` | Singleton | 创建/缓存 AI 聊天客户端 |
| `IAIAgentFactory` | `AIAgentFactory` | Singleton | 构建 AIAgent 实例 |
| `IAIAgentRunner` | `AIAgentRunner` | Singleton | 执行对话（同步/流式） |
| `ISkillManager` | `SkillManager` | Singleton | 技能枚举/执行/上传 |
| `ChatHistoryProvider` | Vector / SqlSugar | Singleton | 会话持久化 |

---

## 8. 相关项目

| 项目 | 说明 |
|------|------|
| `Lzq.Extensions.AI` | 核心 AI Agent 引擎（本库） |
| `Lzq.Extensions.AI.Qdrant` | Qdrant 向量数据库适配（开发中） |

---

## 环境要求

- .NET 8.0+ / .NET 9.0 / .NET 10.0
- 依赖：`Microsoft.Agents.AI`、`ModelContextProtocol`、`OllamaSharp`、`SemanticKernel`
