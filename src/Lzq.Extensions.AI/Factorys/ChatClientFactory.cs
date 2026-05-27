using Lzq.Extensions.AI.Interfaces;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using System.ClientModel;
using System.Collections.Concurrent;

namespace Lzq.Extensions.AI.Factorys;

public class ChatClientFactory: IChatClientFactory, IDisposable
{
    private const int MaxCacheSize = 50;
    private const int CleanupBatchSize = 20;
    private readonly ConcurrentDictionary<string, IChatClient> _chatClientDictionary = new();
    private readonly ConcurrentQueue<string> _creationOrder = new();

    private void ClearAndDisposeClients()
    {
        // 1. 提取所有旧引用
        var oldClients = _chatClientDictionary.Values.ToList();

        // 2. 清空字典和队列
        _chatClientDictionary.Clear();
        _creationOrder.Clear();

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

    private void EvictOldest()
    {
        for (int i = 0; i < CleanupBatchSize; i++)
        {
            if (_creationOrder.TryDequeue(out var configId) &&
                _chatClientDictionary.TryRemove(configId, out var client))
            {
                try { client.Dispose(); }
                catch { }
            }
        }
    }

    public IChatClient GetOrCreate(AISetting aiSetting)
    {
        if (aiSetting == null)
            throw new InvalidOperationException($"参数不能为空");

        // 缓存超过阈值时淘汰最旧的一批
        if (_chatClientDictionary.Count >= MaxCacheSize)
        {
            EvictOldest();
        }

        return _chatClientDictionary.GetOrAdd(aiSetting.ConfigId, key =>
        {
            _creationOrder.Enqueue(key);
            return CreateChatClient(aiSetting);
        });
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
