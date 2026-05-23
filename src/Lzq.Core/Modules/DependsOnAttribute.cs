using Lzq.Core.Modules;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class DependsOnAttribute : Attribute
{
    public Type[] ModuleTypes { get; }

    public DependsOnAttribute(params Type[] moduleTypes)
    {
        foreach (var t in moduleTypes)
        {
            if (!typeof(IModule).IsAssignableFrom(t))
                throw new ArgumentException($"{t.Name} 必须实现 IModule");
        }
        ModuleTypes = moduleTypes;
    }
}