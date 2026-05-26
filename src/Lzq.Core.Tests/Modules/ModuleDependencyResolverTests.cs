using Lzq.Core.Modules;
using Xunit;

namespace Lzq.Core.Tests.Modules;

public class ModuleDependencyResolverTests
{
    private readonly ModuleDependencyResolver _resolver = new();

    // 无依赖，按 Name 排序
    [Fact]
    public void TopologicalSort_SingleModule_ReturnsSingle()
    {
        var modules = new HashSet<Type> { typeof(ModuleA) };
        var sorted = _resolver.TopologicalSort(modules);
        Assert.Single(sorted);
        Assert.Equal(typeof(ModuleA), sorted[0]);
    }

    [Fact]
    public void TopologicalSort_NoDependencies_OrdersByName()
    {
        var modules = new HashSet<Type> { typeof(ModuleC), typeof(ModuleA), typeof(ModuleB) };
        var sorted = _resolver.TopologicalSort(modules);
        Assert.Equal([typeof(ModuleA), typeof(ModuleB), typeof(ModuleC)], sorted);
    }

    // 链式: A -> B -> C
    [Fact]
    public void TopologicalSort_SimpleChain_OrdersDependenciesFirst()
    {
        var modules = new HashSet<Type>
        {
            typeof(ChainA),
            typeof(ChainB),
            typeof(ChainC)
        };
        var sorted = _resolver.TopologicalSort(modules);

        Assert.Equal(3, sorted.Count);
        Assert.True(sorted.IndexOf(typeof(ChainC)) < sorted.IndexOf(typeof(ChainB)));
        Assert.True(sorted.IndexOf(typeof(ChainB)) < sorted.IndexOf(typeof(ChainA)));
    }

    // 菱形: A -> (B, C), B -> D, C -> D
    [Fact]
    public void TopologicalSort_DiamondDependency_ResolvesCorrectly()
    {
        var modules = new HashSet<Type>
        {
            typeof(DiamondA),
            typeof(DiamondB),
            typeof(DiamondC),
            typeof(DiamondD)
        };
        var sorted = _resolver.TopologicalSort(modules);

        // D 必须在 B 和 C 前, B 和 C 必须在 A 前
        Assert.True(sorted.IndexOf(typeof(DiamondD)) < sorted.IndexOf(typeof(DiamondB)));
        Assert.True(sorted.IndexOf(typeof(DiamondD)) < sorted.IndexOf(typeof(DiamondC)));
        Assert.True(sorted.IndexOf(typeof(DiamondB)) < sorted.IndexOf(typeof(DiamondA)));
        Assert.True(sorted.IndexOf(typeof(DiamondC)) < sorted.IndexOf(typeof(DiamondA)));
    }

    // BFS 收集: 从 ChainA 出发应收集 ChainA, ChainB, ChainC
    [Fact]
    public void Resolve_FromTopModule_CollectsAllDependencies()
    {
        var sorted = _resolver.Resolve<ChainA>();
        Assert.Equal(3, sorted.Count);
        Assert.Contains(typeof(ChainA), sorted);
        Assert.Contains(typeof(ChainB), sorted);
        Assert.Contains(typeof(ChainC), sorted);
    }

    [Fact]
    public void Resolve_FromMiddleModule_CollectsOnlyUpstream()
    {
        var sorted = _resolver.Resolve<ChainB>();
        Assert.Equal(2, sorted.Count);
        Assert.Contains(typeof(ChainB), sorted);
        Assert.Contains(typeof(ChainC), sorted);
    }

    // 重复依赖: A -> (B, B), B -> C
    [Fact]
    public void TopologicalSort_DuplicateDependencies_Deduplicates()
    {
        var modules = new HashSet<Type>
        {
            typeof(DupA),
            typeof(DupB),
            typeof(DupC)
        };
        var sorted = _resolver.TopologicalSort(modules);

        Assert.True(sorted.IndexOf(typeof(DupC)) < sorted.IndexOf(typeof(DupB)));
        Assert.True(sorted.IndexOf(typeof(DupB)) < sorted.IndexOf(typeof(DupA)));
    }

    // 循环检测
    [Fact]
    public void TopologicalSort_DirectCycle_Throws()
    {
        var modules = new HashSet<Type> { typeof(CycleA), typeof(CycleB) };
        var ex = Assert.Throws<LzqModuleException>(() => _resolver.TopologicalSort(modules));
        Assert.Contains("\u5faa\u73af\u4f9d\u8d56", ex.Message);
        Assert.Contains(nameof(CycleA), ex.Message);
        Assert.Contains(nameof(CycleB), ex.Message);
    }

    [Fact]
    public void TopologicalSort_IndirectCycle_Throws()
    {
        var modules = new HashSet<Type>
        {
            typeof(CycleX),
            typeof(CycleY),
            typeof(CycleZ)
        };
        var ex = Assert.Throws<LzqModuleException>(() => _resolver.TopologicalSort(modules));
        Assert.Contains("\u5faa\u73af\u4f9d\u8d56", ex.Message);
    }

    [Fact]
    public void TopologicalSort_SelfCycle_Throws()
    {
        var modules = new HashSet<Type> { typeof(SelfCycle) };
        var ex = Assert.Throws<LzqModuleException>(() => _resolver.TopologicalSort(modules));
        Assert.Contains("\u5faa\u73af\u4f9d\u8d56", ex.Message);
    }

    // ---- 测试用模块定义 ----

    private class ModuleA : BaseModule { }
    private class ModuleB : BaseModule { }
    private class ModuleC : BaseModule { }

    [DependsOn(typeof(ChainB))]
    private class ChainA : BaseModule { }

    [DependsOn(typeof(ChainC))]
    private class ChainB : BaseModule { }

    private class ChainC : BaseModule { }

    [DependsOn(typeof(DiamondB), typeof(DiamondC))]
    private class DiamondA : BaseModule { }

    [DependsOn(typeof(DiamondD))]
    private class DiamondB : BaseModule { }

    [DependsOn(typeof(DiamondD))]
    private class DiamondC : BaseModule { }

    private class DiamondD : BaseModule { }

    [DependsOn(typeof(DupB), typeof(DupB))]
    private class DupA : BaseModule { }

    [DependsOn(typeof(DupC))]
    private class DupB : BaseModule { }

    private class DupC : BaseModule { }

    [DependsOn(typeof(CycleB))]
    private class CycleA : BaseModule { }

    [DependsOn(typeof(CycleA))]
    private class CycleB : BaseModule { }

    [DependsOn(typeof(CycleY))]
    private class CycleX : BaseModule { }

    [DependsOn(typeof(CycleZ))]
    private class CycleY : BaseModule { }

    [DependsOn(typeof(CycleX))]
    private class CycleZ : BaseModule { }

    [DependsOn(typeof(SelfCycle))]
    private class SelfCycle : BaseModule { }
}
