using Lzq.Extensions.AI.Interfaces;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;
using System.Collections.Concurrent;

namespace Lzq.Extensions.AI.Services;

public class ChatClientService: IChatClientService, IDisposable
{
    private readonly ConcurrentDictionary<string, IChatClient> _chatClientDictionary = new();

    private void ClearAndDisposeClients()
    {
        // 1. 提取所有旧引用
        var oldClients = _chatClientDictionary.Values.ToList();

        // 2. 清空字典防止新请求拿到旧引用
        _chatClientDictionary.Clear();

        // 3. 逐个释放
        foreach (var client in oldClients)
        {
            try
            {
                // 注意：IChatClient 继承自 IDisposable
                client.Dispose();
            }
            catch (Exception ex)
            {
                // 防止某个 Client 释放失败影响全局
                Console.WriteLine($"Dispose client error: {ex.Message}");
            }
        }
    }

    public IChatClient GetChatClient(AISetting aiSetting)
    {
        if (aiSetting == null)
            throw new InvalidOperationException($"参数不能为空");

        return _chatClientDictionary.GetOrAdd(aiSetting.ConfigId, _ => CreateChatClient(aiSetting));
    }

    private IChatClient CreateChatClient(AISetting aiSetting)
    {
        if (string.IsNullOrWhiteSpace(aiSetting.Url) ||
            string.IsNullOrWhiteSpace(aiSetting.KeySecret) ||
            string.IsNullOrWhiteSpace(aiSetting.Model))
            throw new InvalidOperationException($"ConfigId '{aiSetting.ConfigId}' 配置不完整");

        try
        {
            if (aiSetting.ConfigId.StartsWith("Ollama"))
            {
                return new OllamaApiClient(aiSetting.Url);
            }
            else
            {
                var chatClient = CreateOpenAIChatClient(aiSetting);
                return chatClient.AsIChatClient();
            }
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException($"ConfigId '{aiSetting.ConfigId}' 的Url格式无效: {aiSetting.Url}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"创建ChatClient时发生错误 (ConfigId: {aiSetting.ConfigId})", ex);
        }
    }

    public OpenAI.Chat.ChatClient CreateOpenAIChatClient(AISetting aiSetting) 
    {
        var openAIClientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(aiSetting.Url)
        };

        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(aiSetting.KeySecret),
            openAIClientOptions);

        return openAIClient.GetChatClient(aiSetting.Model);
    }

    public void Dispose()
    {
        ClearAndDisposeClients();
    }
}
