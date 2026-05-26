using Lzq.Core.Modules;
using Lzq.Extensions.Serilog;
using Lzq.Extensions.WebApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddLzqSerilog();

await builder.AddApplicationAsync<WebApiModule>();

var app = builder.Build();

await app.InitializeApplicationAsync();
await app.RunAsync();