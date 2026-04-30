using Lzq.Extensions.AI.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
using System.ClientModel;
using System.Collections.Concurrent;

namespace Lzq.Extensions.AI.Services;

public class ChatClientService: IChatClientService, IDisposable
{
    private readonly ConcurrentDictionary<string, IChatClient> _chatClientDictionary = new();
    private readonly IOptionsMonitor<List<AISetting>> _optionsMonitor;
    private readonly IDisposable? _onChangeToken;

    public ChatClientService(IOptionsMonitor<List<AISetting>> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
        _onChangeToken = _optionsMonitor.OnChange(_ => ClearAndDisposeClients());
    }

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

    public IChatClient GetChatClient(string configId)
    {
        var aiSetting = _optionsMonitor.CurrentValue.FirstOrDefault(x => x.ConfigId == configId);
        if (aiSetting == null)
            throw new InvalidOperationException($"未找到ConfigId: {configId}");

        return _chatClientDictionary.GetOrAdd(configId, _ => CreateChatClient(aiSetting));
    }

    public IChatClient GetChatClient(AISetting aiSetting)
    {
        if (aiSetting == null)
            throw new InvalidOperationException($"参数不能为空");

        return CreateChatClient(aiSetting);
    }

    private IChatClient CreateChatClient(AISetting aiSetting)
    {
        if (string.IsNullOrWhiteSpace(aiSetting.Url))
            throw new InvalidOperationException($"ConfigId '{aiSetting.ConfigId}' 的Url配置不能为空");

        if (string.IsNullOrWhiteSpace(aiSetting.KeySecret))
            throw new InvalidOperationException($"ConfigId '{aiSetting.ConfigId}' 的KeySecret配置不能为空");

        if (string.IsNullOrWhiteSpace(aiSetting.Model))
            throw new InvalidOperationException($"ConfigId '{aiSetting.ConfigId}' 的Model配置不能为空");

        try
        {
            if (aiSetting.ConfigId.StartsWith("Ollama"))
            {
                return new OllamaApiClient(aiSetting.Url);
            }
            else
            {
                var openAIClientOptions = new OpenAIClientOptions
                {
                    Endpoint = new Uri(aiSetting.Url)
                };

                var openAIClient = new OpenAIClient(
                    new ApiKeyCredential(aiSetting.KeySecret),
                    openAIClientOptions);

                var chatClient = openAIClient.GetChatClient(aiSetting.Model);
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

    public void Dispose()
    {
        _onChangeToken?.Dispose();
        ClearAndDisposeClients();
    }
}
