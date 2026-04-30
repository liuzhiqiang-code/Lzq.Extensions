using Lzq.Extensions.ExternalHttpApi;
using Lzq.Extensions.ExternalHttpApi.Aop;
using WebApiClientCore.Attributes;

namespace Lzq.Extensions.WebApi.ExternalHttpApis;

[NeedAuthToken]
[ApiReturnUnwrapper]
[LoggingFilter] // 假设你之前定义的日志拦截器
public interface IGitHubUserApi : IExternalHttpApi
{
    [HttpGet("/users/{username}")]
    Task<object> GetUserAsync([PathQuery] string username);
}