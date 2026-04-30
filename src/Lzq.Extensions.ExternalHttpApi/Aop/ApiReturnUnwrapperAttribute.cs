using System.Text.Json;
using WebApiClientCore;
using WebApiClientCore.Attributes;

namespace Lzq.Extensions.ExternalHttpApi.Aop;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public class ApiReturnUnwrapperAttribute : ApiFilterAttribute
{
    public override async Task OnRequestAsync(ApiRequestContext context)
    {
    }

    public override async Task OnResponseAsync(ApiResponseContext context)
    {
        // 1. 验证 HTTP 状态码
        if (!context.HttpContext!.ResponseMessage!.IsSuccessStatusCode) return;

        // 2. 获取返回类型 (例如 Task<UserDto> 中的 UserDto)
        var returnType = context.ActionDescriptor.Return.ReturnType;

        // 3. 构造 ApiResult<T> 的具体类型
        // 如果你的接口直接返回 ApiResult<T>，则需要特殊处理逻辑
        var wrapperType = typeof(ApiResult<>).MakeGenericType(returnType);

        try
        {
            // 4. 使用 WebApiClientCore 内置的反序列化方法
            var result = await context.JsonDeserializeAsync(wrapperType) as dynamic;

            if (result == null)
            {
                throw new UserFriendlyException("无法解析响应内容" + context.HttpContext.ResponseMessage.ToString());
            }

            // 5. 业务逻辑判断
            if (result.Code != 200) // 假设 200 是成功
            {
                // 抛出异常，会被全局异常拦截器捕获
                throw new Exception($"业务异常: {result.Message} ({result.Code})");
            }

            // 6. 关键步骤：将解包后的 Data 赋值回 Result，供后续业务使用
            context.Result = result.Data;
        }
        catch (JsonException ex)
        {
            throw new Exception("响应格式非标准的 ApiResult 结构", ex);
        }
    }
}