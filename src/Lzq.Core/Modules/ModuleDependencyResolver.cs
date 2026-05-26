using System.Reflection;

namespace Lzq.Core.Modules;

internal class ModuleDependencyResolver
{
    public List<Type> Resolve<TStartup>() where TStartup : IModule
    {
        var allModules = CollectModuleTypes(typeof(TStartup));
        return TopologicalSort(allModules);
    }

    private static HashSet<Type> CollectModuleTypes(Type startupType)
    {
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(startupType);
        visited.Add(startupType);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            var deps = type.GetCustomAttributes<DependsOnAttribute>()
                .SelectMany(attr => attr.ModuleTypes);

            foreach (var dep in deps)
            {
                if (visited.Add(dep))
                    queue.Enqueue(dep);
            }
        }

        return visited;
    }

    public List<Type> TopologicalSort(HashSet<Type> allModules)
    {
        var graph = new Dictionary<Type, List<Type>>();
        var indegree = new Dictionary<Type, int>();

        foreach (var type in allModules)
        {
            graph[type] = new List<Type>();
            indegree[type] = 0;
        }

        foreach (var type in allModules)
        {
            var deps = type.GetCustomAttributes<DependsOnAttribute>()
                .SelectMany(attr => attr.ModuleTypes)
                .Where(allModules.Contains);

            foreach (var dep in deps)
            {
                graph[dep].Add(type);
                indegree[type]++;
            }
        }

        var queue = new Queue<Type>(indegree
            .Where(kv => kv.Value == 0)
            .OrderBy(kv => kv.Key.Name)
            .Select(kv => kv.Key));

        var sorted = new List<Type>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            sorted.Add(node);

            foreach (var neighbor in graph[node].OrderBy(t => t.Name))
            {
                indegree[neighbor]--;
                if (indegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (sorted.Count != allModules.Count)
        {
            var cyclePath = FindCycle(allModules);
            var cycleStr = string.Join(" \u2192 ", cyclePath!.Select(t => t.Name));
            throw new LzqModuleException($"\u68c0\u6d4b\u5230\u6a21\u5757\u5faa\u73af\u4f9d\u8d56: {cycleStr}");
        }

        return sorted;
    }

    public List<Type>? FindCycle(HashSet<Type> allModules)
    {
        var graph = new Dictionary<Type, List<Type>>();
        foreach (var type in allModules)
            graph[type] = new List<Type>();

        foreach (var type in allModules)
        {
            var deps = type.GetCustomAttributes<DependsOnAttribute>()
                .SelectMany(attr => attr.ModuleTypes)
                .Where(allModules.Contains);
            foreach (var dep in deps)
                graph[dep].Add(type);
        }

        var visited = new HashSet<Type>();
        var recStack = new HashSet<Type>();
        var pathStack = new List<Type>();

        foreach (var node in allModules.OrderBy(t => t.Name))
        {
            if (DfsCycleDetect(node, graph, visited, recStack, pathStack, out var cycle))
                return cycle;
        }

        return null;
    }

    private static bool DfsCycleDetect(
        Type node,
        Dictionary<Type, List<Type>> graph,
        HashSet<Type> visited,
        HashSet<Type> recStack,
        List<Type> pathStack,
        out List<Type>? cycle)
    {
        cycle = null;

        if (recStack.Contains(node))
        {
            var idx = pathStack.IndexOf(node);
            cycle = pathStack.Skip(idx).Concat(new[] { node }).ToList();
            return true;
        }

        if (visited.Contains(node))
            return false;

        visited.Add(node);
        recStack.Add(node);
        pathStack.Add(node);

        foreach (var neighbor in graph[node])
        {
            if (DfsCycleDetect(neighbor, graph, visited, recStack, pathStack, out cycle))
                return true;
        }

        recStack.Remove(node);
        pathStack.RemoveAt(pathStack.Count - 1);
        return false;
    }
}
