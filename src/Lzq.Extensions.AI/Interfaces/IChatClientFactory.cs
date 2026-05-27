using Microsoft.Extensions.AI;

namespace Lzq.Extensions.AI.Interfaces;

/// <summary>
/// AI聊天客户端服务接口
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// 获取聊天客户端实例
    /// </summary>
    /// <param name="configId">配置ID</param>
    /// <returns>聊天客户端实例</returns>
    IChatClient GetOrCreate(AISetting setting);

    /// <summary>
    /// OpenAIChatClient
    /// </summary>
    /// <param name="aiSetting"></param>
    /// <returns></returns>
    OpenAI.Chat.ChatClient CreateOpenAIChatClient(AISetting aiSetting);
}