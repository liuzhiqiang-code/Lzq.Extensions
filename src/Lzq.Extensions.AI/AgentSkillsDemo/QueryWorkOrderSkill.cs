using Lzq.Extensions.AI.AgentSkills;
using Microsoft.Agents.AI;
using System.ComponentModel;
using System.Text.Json;

namespace Lzq.Extensions.AI.AgentSkillsDemo;

/// <summary>
/// 工单查询技能 —— 演示如何通过 AgentSkillScript 提供动态查询能力
/// </summary>
[GeneralSkill(SkillCategory.Office)]
public class QueryWorkOrderSkill : LzqAgentSkillBase<QueryWorkOrderSkill>
{
    // ==================== 假数据 ====================
    private static readonly List<WorkOrder> DemoOrders = new()
    {
        new() { Code = "WO-20260501-001", Title = "注塑机A-01 停机故障", Status = "completed", Progress = 100, Operator = "张伟", CreateTime = "2026-05-01 08:30" },
        new() { Code = "WO-20260510-001", Title = "CNC-02 刀具磨损更换", Status = "processing", Progress = 60, Operator = "李丽", CreateTime = "2026-05-10 09:00" },
        new() { Code = "WO-20260510-002", Title = "烘干机B-03 温度异常", Status = "pending", Progress = 0, Operator = "王强", CreateTime = "2026-05-10 10:15" },
        new() { Code = "WO-20260509-001", Title = "冲压机C-01 模具检修", Status = "completed", Progress = 100, Operator = "赵华", CreateTime = "2026-05-09 14:00" },
        new() { Code = "WO-20260508-001", Title = "注塑机A-02 参数调校", Status = "completed", Progress = 100, Operator = "张伟", CreateTime = "2026-05-08 16:45" },
    };

    // ==================== 技能元数据 ====================
    protected override string SkillName => "work-order-demo";
    protected override string SkillDescription => "提供生产系统工单详情、实时生产进度查询、工单列表检索能力。";

    // ==================== 核心指令 ====================
    protected override string Instructions => """
        你是一个工单管理专家。当用户询问工单相关问题时，请按以下指引操作：

        1. 若用户提供工单号（格式：WO-YYYYMMDD-序号），调用 GetProgress 脚本查询该工单详情。
        2. 若用户要求列出全部工单或按状态筛选（如「有哪些待处理的工单」），调用 ListOrders 脚本获取列表。
        3. 若用户提及某操作员（如「张伟负责哪些工单」），调用 ListOrders 脚本并按人员筛选。
        4. 展示结果时，结合 work-order-rules 资源中的状态说明，用通俗语言解释当前状态含义。
        5. 若传入的工单号格式不正确，请提醒用户正确的格式。
        """;

    // ==================== 资源：规则说明 ====================
    [AgentSkillResource("work-order-rules")]
    [Description("工单格式说明和状态定义")]
    public static string WorkOrderRules => """
        ## 工单号格式
        - 格式：WO-年月日-序号
        - 示例：WO-20260510-001

        ## 状态定义
        | 状态 | 含义 | 说明 |
        |------|------|------|
        | pending | 待处理 | 工单已创建，尚未开始 |
        | processing | 进行中 | 正在处理中 |
        | completed | 已完成 | 工单已关闭 |
        """;

    // ==================== 脚本：查询单个工单 ====================
    [AgentSkillScript("GetProgress")]
    [Description("通过工单号查询详细的生产进度。参数 workOrder 格式为 WO-YYYYMMDD-序号。")]
    public Task<string> GetProgressAsync(
        [Description("工单号，示例：WO-20260510-001")] string workOrder)
    {
        var order = DemoOrders.FirstOrDefault(o => o.Code == workOrder);

        if (order is null)
        {
            return Task.FromResult($"未找到工单 {workOrder}，请检查工单号是否正确。");
        }

        var result = new
        {
            order.Code,
            order.Title,
            order.Status,
            StatusText = order.Status switch
            {
                "pending" => "待处理",
                "processing" => "进行中",
                "completed" => "已完成",
                _ => order.Status
            },
            order.Progress,
            order.Operator,
            order.CreateTime
        };

        return Task.FromResult(JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }

    // ==================== 脚本：查询工单列表 ====================
    [AgentSkillScript("ListOrders")]
    [Description("获取工单列表，可按状态和操作员筛选。不传参数则返回全部。")]
    public Task<string> ListOrdersAsync(
        [Description("状态筛选，可选值：pending / processing / completed，不传返回全部")] string? status = null,
        [Description("操作员姓名模糊筛选，不传返回全部")] string? operatorName = null)
    {
        var query = DemoOrders.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(operatorName))
            query = query.Where(o => o.Operator.Contains(operatorName));

        var list = query.Select(o => new
        {
            o.Code,
            o.Title,
            StatusText = o.Status switch
            {
                "pending" => "待处理",
                "processing" => "进行中",
                "completed" => "已完成",
                _ => o.Status
            },
            o.Progress,
            o.Operator,
            o.CreateTime
        }).ToList();

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            Total = list.Count,
            Orders = list,
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }

    // ==================== 内部实体 ====================
    private class WorkOrder
    {
        public string Code { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = "pending";
        public int Progress { get; init; }
        public string Operator { get; init; } = string.Empty;
        public string CreateTime { get; init; } = string.Empty;
    }
}