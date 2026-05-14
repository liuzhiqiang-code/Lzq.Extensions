using Microsoft.Extensions.DependencyInjection;

namespace Lzq.Extensions.AI.AgentSkills;

public static class SkillsExtensions
{
    public static void AddLzqAgentSkills(this IServiceCollection services)
    {
        // 注册为单例，负责全生命周期的文件监听
        services.AddScoped<AgentSkillProvider>();
        services.AddScoped<ISkillManager, SkillManager>();
    }

    //public static void UseLzqAgentSkills(this IApplicationBuilder app)
    //{
    //    var endpoint = (IEndpointRouteBuilder)app;
    //    var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();

    //    // 1. 查询接口：返回当前加载的所有工具元数据
    //    endpoint.MapGet("/api/ai/skills", (AgentSkillProvider provider) =>
    //    {
    //        var skillInstances = provider.GetSkills();

    //        var results = skillInstances.Select(skill =>
    //        {
    //            var type = skill.GetType();

    //            var tools = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
    //                .Select(m => new { Method = m, Attr = m.GetCustomAttribute<AgentSkillScriptAttribute>() })
    //                .Where(x => x.Attr != null)
    //                .Select(x => new
    //                {
    //                    ToolName = x.Attr!.Name ?? x.Method.Name,
    //                    Description = x.Method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "无描述",
    //                    Parameters = x.Method.GetParameters().Select(p => new
    //                    {
    //                        p.Name,
    //                        ParameterType = p.ParameterType.Name,
    //                        Description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "无描述"
    //                    })
    //                })
    //                .ToList();

    //            return new
    //            {
    //                SkillName = skill.Frontmatter.Name, // 插件大类名
    //                SkillDescription = skill.Frontmatter.Description,
    //                Tools = tools // 下面直接是工具列表
    //            };
    //        });

    //        return Results.Ok(results);
    //    }).WithTags("AI Skills").RequireAuthorization("AdminOnly");

    //    // 2. 执行接口：动态调用指定的插件方法
    //    endpoint.MapPost("/api/ai/skills/execute", async (
    //        ExecuteSkillRequest request,
    //        AgentSkillProvider provider) =>
    //    {
    //        var logger = loggerFactory.CreateLogger("AgentSkills");
    //        // 使用强类型方法按名称获取技能，避免遍历
    //        var skillInstance = provider.GetSkillByName(request.SkillName);
    //        if (skillInstance == null)
    //            return Results.NotFound($"未找到技能: {request.SkillName}");

    //        // B. 寻找匹配的方法
    //        // 逻辑：匹配 AgentSkillScript 特性中的 Name 属性
    //        var method = skillInstance.GetType()
    //            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
    //            .FirstOrDefault(m =>
    //            {
    //                var attr = m.GetCustomAttribute<AgentSkillScriptAttribute>();
    //                // 此时特性里的 Name 就是我们要找的 ToolName
    //                return attr != null && (attr.Name ?? m.Name).Equals(request.ToolName, StringComparison.OrdinalIgnoreCase);
    //            });

    //        if (method == null)
    //            return Results.NotFound($"技能 {request.SkillName} 中未找到标识为 {request.ToolName} 的函数");

    //        try
    //        {
    //            // C. 参数准备
    //            var methodParams = method.GetParameters();
    //            var args = new object?[methodParams.Length];

    //            for (int i = 0; i < methodParams.Length; i++)
    //            {
    //                var p = methodParams[i];
    //                if (request.Arguments != null && request.Arguments.TryGetValue(p.Name!, out var val))
    //                {
    //                    if (val == null)
    //                    {
    //                        args[i] = null;
    //                    }
    //                    else
    //                    {
    //                        // 处理可空类型转换 (如 int?)
    //                        var targetType = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
    //                        args[i] = Convert.ChangeType(val.ToString(), targetType);
    //                    }
    //                }
    //                else if (p.HasDefaultValue)
    //                {
    //                    args[i] = p.DefaultValue;
    //                }
    //            }

    //            // D. 执行并处理异步结果
    //            object? result = method.Invoke(skillInstance, args);

    //            if (result is Task task)
    //            {
    //                await task;
    //                // 获取异步返回的值 (Task<T>)
    //                var resultProperty = task.GetType().GetProperty("Result");
    //                return Results.Ok(resultProperty?.GetValue(task));
    //            }

    //            return Results.Ok(result);
    //        }
    //        catch (TargetInvocationException ex)
    //        {
    //            // 记录完整异常，只返回通用错误
    //            logger.LogError(ex.InnerException, "技能调用失败: {SkillName}.{ToolName}", request.SkillName, request.ToolName);
    //            // 返回业务代码抛出的真实异常信息
    //            return Results.BadRequest($"业务执行失败: {ex.InnerException?.Message ?? ex.Message}");
    //        }
    //        catch (Exception ex)
    //        {
    //            logger.LogError(ex, "技能执行异常: {SkillName}.{ToolName}", request.SkillName, request.ToolName);
    //            return Results.BadRequest($"执行异常: {ex.Message}");
    //        }
    //    }).WithTags("AI Skills").RequireAuthorization("AdminOnly");
    //}

    //public record ExecuteSkillRequest(string SkillName, string ToolName, Dictionary<string, object>? Arguments);
}