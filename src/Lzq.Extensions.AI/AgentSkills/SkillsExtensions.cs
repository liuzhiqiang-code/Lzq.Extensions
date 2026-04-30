using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Reflection;

namespace Lzq.Extensions.AI.AgentSkills;

public static class SkillsExtensions
{
    public static void AddLzqAgentSkills(this IServiceCollection services)
    {
        // 注册为单例，负责全生命周期的文件监听
        services.AddSingleton<AgentSkillProvider>();
    }

    public static void UseLzqAgentSkills(this IApplicationBuilder app)
    {
        var endpoint = (IEndpointRouteBuilder)app;

        // 1. 查询接口：返回当前加载的所有工具元数据
        endpoint.MapGet("/api/ai/skills", (AgentSkillProvider provider) =>
        {
            var skillInstances = provider.GetSkills();

            var results = skillInstances.Select(s =>
            {
                var type = s.GetType();
                var fm = (s as dynamic).Frontmatter;

                // 扁平化提取：一个方法就是一个 Tool
                var tools = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                    .Select(m => new
                    {
                        Method = m,
                        Attr = m.GetCustomAttribute<AgentSkillScriptAttribute>()
                    })
                    .Where(x => x.Attr != null)
                    .Select(x => new
                    {
                        // 特性里的 Name 直接作为 ToolName
                        ToolName = x.Attr!.Name ?? x.Method.Name,
                        Description = x.Method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "无描述",
                        Parameters = x.Method.GetParameters().Select(p => new
                        {
                            p.Name,
                            ParameterType = p.ParameterType.Name,
                            Description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "无描述"
                        })
                    })
                    .ToList();

                return new
                {
                    SkillName = fm.Name, // 插件大类名
                    SkillDescription = fm.Description,
                    Tools = tools // 下面直接是工具列表
                };
            });

            return Results.Ok(results);
        }).WithTags("AI Skills").AllowAnonymous();

        // 2. 执行接口：动态调用指定的插件方法
        endpoint.MapPost("/api/ai/skills/execute", async (
            ExecuteSkillRequest request,
            AgentSkillProvider provider) =>
        {
            // A. 寻找匹配的技能实例 (SkillName 对应插件的 Frontmatter.Name)
            var skillInstance = provider.GetSkills().FirstOrDefault(s =>
                (s as dynamic).Frontmatter.Name.Equals(request.SkillName, StringComparison.OrdinalIgnoreCase));

            if (skillInstance == null)
                return Results.NotFound($"未找到技能: {request.SkillName}");

            // B. 寻找匹配的方法
            // 逻辑：匹配 AgentSkillScript 特性中的 Name 属性
            var method = skillInstance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    var attr = m.GetCustomAttribute<AgentSkillScriptAttribute>();
                    // 此时特性里的 Name 就是我们要找的 ToolName
                    return attr != null && (attr.Name ?? m.Name).Equals(request.ToolName, StringComparison.OrdinalIgnoreCase);
                });

            if (method == null)
                return Results.NotFound($"技能 {request.SkillName} 中未找到标识为 {request.ToolName} 的函数");

            try
            {
                // C. 参数准备
                var methodParams = method.GetParameters();
                var args = new object?[methodParams.Length];

                for (int i = 0; i < methodParams.Length; i++)
                {
                    var p = methodParams[i];
                    if (request.Arguments != null && request.Arguments.TryGetValue(p.Name!, out var val))
                    {
                        if (val == null)
                        {
                            args[i] = null;
                        }
                        else
                        {
                            // 处理可空类型转换 (如 int?)
                            var targetType = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                            args[i] = Convert.ChangeType(val.ToString(), targetType);
                        }
                    }
                    else if (p.HasDefaultValue)
                    {
                        args[i] = p.DefaultValue;
                    }
                }

                // D. 执行并处理异步结果
                object? result = method.Invoke(skillInstance, args);

                if (result is Task task)
                {
                    await task;
                    // 获取异步返回的值 (Task<T>)
                    var resultProperty = task.GetType().GetProperty("Result");
                    return Results.Ok(resultProperty?.GetValue(task));
                }

                return Results.Ok(result);
            }
            catch (TargetInvocationException ex)
            {
                // 返回业务代码抛出的真实异常信息
                return Results.BadRequest($"业务执行失败: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"执行异常: {ex.Message}");
            }
        }).WithTags("AI Skills").AllowAnonymous();
    }

    public record ExecuteSkillRequest(string SkillName, string ToolName, Dictionary<string, object>? Arguments);
}