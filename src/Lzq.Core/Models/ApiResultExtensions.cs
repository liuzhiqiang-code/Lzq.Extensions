namespace Lzq.Core.Models;

public static class ApiResultExtensions
{
    /// <summary>
    /// 获取 ApiResult<T> 中的数据
    /// </summary>
    public static T? GetData<T>(this ApiResult result)
    {
        if (result is ApiResult<T> typedResult)
        {
            return typedResult.Data;
        }
        return default;
    }

    /// <summary>
    /// 获取数据，类型不匹配时抛出异常
    /// </summary>
    public static T GetDataOrThrow<T>(this ApiResult result)
    {
        if (result is ApiResult<T> typedResult)
        {
            return typedResult.Data ?? throw new InvalidOperationException("Data is null");
        }
        throw new InvalidOperationException($"Cannot cast ApiResult to ApiResult<{typeof(T).Name}>");
    }
}
