using Lzq.Extensions.WebApi.ExternalHttpApis;
using NSwag.Annotations;

namespace Lzq.Extensions.WebApi.Services;

/// <summary>
/// 外部 HTTP 接口集成测试服务
/// </summary>
public class ExternalApiTestService : ServiceBase
{
    private IGitHubUserApi GithubApi => GetRequiredService<IGitHubUserApi>();

    public ExternalApiTestService() : base("/api/v1/test/external") { }

    [OpenApiTag("ExternalTest", Description = "测试外部接口调用（GitHub）")]
    [RoutePattern(pattern: "github/{username}", true)]
    public async Task<IResult> GetRemoteUser(string username)
    {
        // 这里会触发：
        // 1. 自动根据配置注入的 HttpHost 发起请求
        // 2. HttpContextHeaderFilter 自动透传当前请求的 Authorization 和 TraceId
        // 3. ApiReturnUnwrapper 自动解析返回结果并解包 Data 字段
        var result = await GithubApi.GetUserAsync(username);

        return Results.Ok(ApiResult.Success(result));
    }

    [OpenApiTag("ExternalTest", Description = "模拟带异常的外部调用")]
    [RoutePattern(pattern: "error-check", true)]
    public async Task<IResult> TestErrorHandling()
    {
        // 故意调用一个不存在的用户或触发业务异常
        // 验证 ApiReturnUnwrapper 是否能正确抛出 Exception 
        // 并被全局异常拦截器捕获
        try
        {
            await GithubApi.GetUserAsync("non-existent-user-lzq-test-123456");
            return Results.Ok();
        }
        catch (Exception ex)
        {
            // 这里的异常会被框架捕获并返回给前端
            throw new UserFriendlyException($"外部接口捕获到异常: {ex.Message}");
        }
    }
}