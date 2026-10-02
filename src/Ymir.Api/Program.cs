using Ymir.Api.Endpoints;
using Ymir.Platform.Infrastructure;
using Ymir.VibeMaker.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddPlatformInfrastructure();
builder.Services.AddVibeMakerInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

var app = builder.Build();

// 錯誤回應不得暴露 stack trace / host path（SA §12），一律使用 ProblemDetails。
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDevAgentEndpoints();
}

app.MapDefaultEndpoints();

app.Run();

/// <summary>讓整合測試可以使用 <c>WebApplicationFactory&lt;Program&gt;</c>。</summary>
public partial class Program;
