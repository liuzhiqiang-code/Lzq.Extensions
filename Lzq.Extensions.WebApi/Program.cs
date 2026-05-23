using Lzq.Core;
using Lzq.Core.Modules;
using Lzq.Extensions.AI;
using Lzq.Extensions.ExternalHttpApi;
using Lzq.Extensions.Jwt;
using Lzq.Extensions.NSwag;
using Lzq.Extensions.SqlSugar;
using Lzq.Extensions.WebApi;
using Lzq.Extensions.Serilog;


var builder = WebApplication.CreateBuilder(args);

builder.AddLzqSerilog();

builder.SerializationModules()
    // 模块注册顺序：核心模块放在最前面，WebApi模块放在最后面，业务模块根据需要放在中间。
    .AddModule<CoreModule>()
    .AddModule<NSwagModule>()
    .AddModule<ExternalHttpApiModule>()
    .AddModule<JwtModule>()
    .AddModule<SqlSugarModule>()
    .AddModule<AIModule>()

    // 

    
    .AddModule<WebApiModule>()
    .ConfigureModules();

var app = builder.Build();
app.UseSerializationModules();

app.Run();