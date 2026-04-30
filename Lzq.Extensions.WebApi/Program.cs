using Lzq.Core;
using Lzq.Extensions.AI;
using Lzq.Extensions.AI.AgentSkills;
using Lzq.Extensions.ExternalHttpApi;
using Lzq.Extensions.Jwt;
using Lzq.Extensions.NSwag;
using Lzq.Extensions.SqlSugar;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCoreAssembly().AddMapster().AddCoreAutoInject();

// Add services to the container.
builder.Services.AddLzqNSwag(options =>
{
    options.Title = "My API";
    options.Version = "2.0.0";
    options.EnableSwaggerUI = !builder.Environment.IsProduction();

    // 启用密码保护
    options.EnableSwaggerUIPassword = true;
    options.SwaggerUIPassword = "123456";
    options.SwaggerUIPasswordCookieExpirationMinutes = 720; // 12小时
});

builder.Services.AddExternalHttpApis(builder.Configuration);

// jwt
builder.Services.AddLzqJwt(builder.Configuration, options =>
{
    options.Issuer = "your-app";
    options.Audience = "your-app";
    options.SecurityKey = "your-secret-key-at-least-16-chars";
});

builder.Services.AddLzqSqlSugar(builder.Configuration);

builder.Services.AddLzqAI()
    .AddSqlSugarChatHistoryProvider()
    .AddLzqAgentSkills();

builder.Services.AddCoreMinimalAPIs();// 一定是Build前最后添加

var app = builder.Build();

app.UseCoreExceptionHandler();

app.UseLzqNSwag();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapMasaMinimalAPIs();


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
});

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
