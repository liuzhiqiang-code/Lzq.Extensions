using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.Collections.Concurrent;

namespace Lzq.Extensions.AI.Provider;

public class McpToolProvider : IDisposable
{
    private readonly ConcurrentDictionary<string, McpClientCache> _clients = new();
    private readonly ConcurrentDictionary<string, Task<McpClientCache>> _creationTasks = new();

    public async Task<IList<AITool>> BuildAIToolsAsync(List<McpModel> selectedMcpModels)
    {
        var mcpTools = new List<AITool>();

        foreach (var mcpModel in selectedMcpModels)
        {
            if (mcpModel.McpType == McpTypeEnum.Http && !string.IsNullOrEmpty(mcpModel.Url))
            {
                var hostedMcpServerTool = new HostedMcpServerTool(
                    serverName: mcpModel.Name,
                    serverAddress: mcpModel.Url)
                {
                    AllowedTools = mcpModel.AllowedTools,
                    ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
                    Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "User-Agent", "LzqAgentForge" }
                    }
                };
                mcpTools.Add(hostedMcpServerTool);
            }
            else
            {
                // Stdio 方式：异步获取或创建
                var cache = await GetOrCreateAsync(mcpModel);

                var clientTools = await cache.Client.ListToolsAsync();

                foreach (var tool in clientTools)
                {
                    if (mcpModel.AllowedTools is null || mcpModel.AllowedTools.Count == 0 ||
                        mcpModel.AllowedTools.Contains(tool.Name))
                    {
                        mcpTools.TryAdd(tool);
                    }
                }
            }
        }

        return mcpTools;
    }

    /// <summary>
    /// ConcurrentDictionary 的异步 GetOrAdd
    /// </summary>
    private async Task<McpClientCache> GetOrCreateAsync(McpModel model)
    {
        if (_clients.TryGetValue(model.Name, out var existing))
            return existing;

        // 用 Task 做锁，保证同 key 只创建一次
        var creationTask = _creationTasks.GetOrAdd(model.Name, _ => CreateMcpClientCacheAsync(model));

        try
        {
            var created = await creationTask;
            _clients.TryAdd(model.Name, created);
            return created;
        }
        finally
        {
            _creationTasks.TryRemove(model.Name, out _);
        }
    }

    private static async Task<McpClientCache> CreateMcpClientCacheAsync(McpModel model)
    {
        var transport = new StdioClientTransport(new()
        {
            Name = model.Name,
            Command = model.Command,
            Arguments = model.Arguments ?? [],
        });

        var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            InitializationTimeout = TimeSpan.FromSeconds(3)
        });

        return new McpClientCache(client);
    }

    public void Dispose()
    {
        foreach (var cache in _clients.Values)
        {
            try
            {
                cache.Client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }
        }
        _clients.Clear();
    }

    private class McpClientCache
    {
        public McpClient Client { get; }

        public McpClientCache(McpClient client)
        {
            Client = client;
        }
    }
}