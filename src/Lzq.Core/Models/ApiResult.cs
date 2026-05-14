using System.Text.Json.Serialization;

/// <summary>
/// 基础响应结果 (仅包含状态码和消息，不包含数据)
/// 用于失败响应或不需要返回数据的成功响应
/// </summary>
public record ApiResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess => Code == 0;

    /// <summary>
    /// 状态码 0 成功  其他 失败
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; }

    // 【关键修改】基类中移除 Data 属性，彻底消除二义性。

    // ==========================================
    // 静态工厂方法 (统一入口)
    // ==========================================

    /// <summary>
    /// 1. 返回成功 - 无数据
    /// </summary>
    public static ApiResult Success(string message = "")
    {
        return new ApiResult { Code = 0, Message = message };
    }

    /// <summary>
    /// 2. 返回成功 - 带数据 (泛型)
    /// </summary>
    public static ApiResult<T> Success<T>(T data, string message = "")
    {
        return new ApiResult<T> { Code = 0, Message = message, Data = data };
    }

    /// <summary>
    /// 3. 返回失败 - 无数据 (通常失败不需要带数据)
    /// </summary>
    public static ApiResult Fail(string message, int code = 1)
    {
        return new ApiResult { Code = code, Message = message };
    }

    /// <summary>
    /// 4. 返回失败 - 带特定泛型类型 (为了满足接口返回值类型要求)
    /// 例如：接口定义返回 ApiResult<User>，但发生了错误，需要返回同类型对象
    /// </summary>
    public static ApiResult<T> Fail<T>(string message, int code = 1, T? data = default)
    {
        return new ApiResult<T> { Code = code, Message = message, Data = data };
    }
}

/// <summary>
/// 泛型响应结果 (继承基类，额外增加 Data 字段)
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public record ApiResult<T> : ApiResult
{
    /// <summary>
    /// 数据载荷
    /// 这里是唯一的 Data 定义，没有任何隐藏或重写，干净纯粹
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

/// <summary>
/// ApiResult 扩展方法
/// </summary>
public static class ApiResultExtensions
{
    /// <summary>
    /// 获取 ApiResult&lt;T&gt; 中的数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">ApiResult实例</param>
    /// <returns>数据对象，如果失败或类型不匹配返回null</returns>
    public static T? GetData<T>(this ApiResult result)
    {
        if (result is ApiResult<T> typedResult)
        {
            return typedResult.Data;
        }
        return default;
    }

    /// <summary>
    /// 获取 ApiResult&lt;T&gt; 中的数据，如果类型不匹配则抛出异常
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">ApiResult实例</param>
    /// <returns>数据对象</returns>
    /// <exception cref="InvalidOperationException">当类型不匹配时抛出</exception>
    public static T GetDataOrThrow<T>(this ApiResult result)
    {
        if (result is ApiResult<T> typedResult)
        {
            return typedResult.Data ?? throw new InvalidOperationException("Data is null");
        }
        throw new InvalidOperationException($"Cannot cast ApiResult to ApiResult<{typeof(T).Name}>");
    }
}