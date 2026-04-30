using System;
using System.Collections.Generic;
using System.Text;

namespace Lzq.Extensions.WebApiClientCore;

[AttributeUsage(AttributeTargets.Interface)]
public class ExternalHttpApiConfigAttribute : Attribute
{
    public string ConfigKey { get; }
    public ExternalHttpApiConfigAttribute(string configKey) => ConfigKey = configKey;
}