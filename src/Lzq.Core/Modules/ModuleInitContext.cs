using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Lzq.Core.Modules;

public class ModuleInitContext
{
    public WebApplication App { get; }
    public ILogger Logger { get; }

    public ModuleInitContext(WebApplication app,ILogger logger)
    {
        App = app;
        Logger = logger;
    }
}