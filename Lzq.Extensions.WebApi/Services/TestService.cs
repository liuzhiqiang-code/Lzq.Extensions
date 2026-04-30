using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace Lzq.Extensions.WebApi.Services;

/// <summary>
/// 各种并发情况下事务完整性的测试用例
/// </summary>
public class TestService : ServiceBase
{
    public TestService() : base("/api/v1/test") { }

    [OpenApiTag("Test", Description = "测试MiniApi")]
    [RoutePattern(pattern: "miniApi", true)]
    public IResult MiniApi([FromQuery] string key)
    {
        return Results.Ok(
            ApiResult.Success(
                new { 
                key,
                threadId = Thread.CurrentThread.ManagedThreadId,
            })
        );
    }

    [OpenApiTag("Test", Description = "测试MiniApi异常")]
    [RoutePattern(pattern: "miniApi/error", true)]
    public IResult MiniApiError([FromQuery] string key)
    {
        return Results.BadRequest(
            ApiResult.Fail("异常返回", 400)
        );
    }

    [OpenApiTag("Test", Description = "测试MiniApi异常")]
    [RoutePattern(pattern: "miniApi/exception", true)]
    public IResult MiniApiException([FromQuery] int key)
    {
        if (key == 200) throw new UserFriendlyException("友好异常");
        if (key == 400) throw new MasaArgumentException("参数异常");
        if (key == 298) throw new MasaValidatorException("验证异常");
        if (key == 401) throw new UnauthorizedAccessException("未授权异常");
        if (key == 404) throw new KeyNotFoundException("未找到异常");
        return Results.Ok();
    }

}