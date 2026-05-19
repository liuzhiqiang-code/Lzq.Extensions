## 🔧 Lzq.Extensions.AI 扩展库 — AgentSkill 技能引擎

基于 **Microsoft.Agents.AI** 构建的技能引擎，支持两类技能接入：

### 1️⃣ 内部 C# AgentSkill（强类型 · DI注入 · 实时调用）

```csharp
// 继承 LzqAgentSkillBase<TSelf>，自动注册到 AgentSkillProvider
[GeneralSkill]  // ← 标记为通用技能（每次对话自动加载）
public class QueryWorkOrderSkill : LzqAgentSkillBase<QueryWorkOrderSkill>
{
    protected override string SkillName => "work-order-query";
    protected override string SkillDescription => "工单详情、进度、列表、统计";

    // 通过 DI 注入 MES 业务服务
    public QueryWorkOrderSkill(
        IWorkOrderService workOrderService,
        IWorkOrderStatisticsService statisticsService) { ... }

    // AgentSkillScript 标记的每个方法 = AI 可调用的工具
    [AgentSkillScript("GetProgress")]
    [Description("通过工单号查询工单详情与生产进度")]
    public async Task<string> GetProgressAsync(string workOrderCode) { ... }
}
```

**核心能力：**

| 组件 | 说明 |
|------|------|
| `LzqAgentSkillBase<TSelf>` | 技能基类，定义 SkillName/Description/Instructions |
| `AgentSkillProvider` | 运行时扫描所有 `LzqAgentSkillBase` 子类，支持 DLL 热加载 |
| `GeneralSkillAttribute` | 标记为通用技能，每次会话自动注入 |
| `ISkillManager` | 技能管理接口：枚举 / 执行 / 上传 DLL / 上传外部 ZIP |
| `AIAgentService` | Agent 运行时，创建 Agent + 绑定技能 + 流式对话 |

### 2️⃣ 外部 Skill（Markdown 知识库 · 零代码接入 · 热加载）

外部 Skill 是一个包含 `SKILL.md` 的目录，通过 ZIP 上传或直接放入 `ExternalSkills/` 目录即可生效：

```
ExternalSkills/
├── lzq-dev-handbook/          ← 后端开发规范
│   ├── SKILL.md               ← name + description
│   ├── specs/                 ← 架构/核心库/模块/质量规范
│   └── extensions/            ← 12 个扩展库详细文档
├── mes-operation-handbook/    ← MES 业务操作手册
│   ├── SKILL.md
│   └── operations/            ← 工单/设备/质检/物料/报工
└── vben-frontend-dev/         ← 前端开发规范
    ├── SKILL.md
    ├── patterns/              ← 4 种页面模式模板
    └── components/            ← 7 个组件用法文档
```

**特点：**
- 零代码：只需 Markdown 文件，AI 自动阅读理解
- 热加载：`FileSystemWatcher` 监听，300ms 防抖，无需重启
- 安全：外部技能脚本执行已禁用（`NoOpScriptRunner`）
- 边界可控：AI 只能读取 Skill 文档内容，无法访问未授权资源

---