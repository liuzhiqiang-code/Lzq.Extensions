namespace Lzq.Extensions.ExternalHttpApi;

[AttributeUsage(AttributeTargets.Interface)]
public class ExternalHttpApiConfigAttribute : Attribute
{
    public string ConfigKey { get; }
    public ExternalHttpApiConfigAttribute(string configKey) => ConfigKey = configKey;
}