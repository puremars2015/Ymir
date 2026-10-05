using System.Text.Json.Serialization;
using Ymir.Api.Auth;
using Ymir.Api.Endpoints;
using Ymir.Api.Infrastructure;
using Ymir.Api.Problems;
using Ymir.Edge;
using Ymir.Platform.Infrastructure;
using Ymir.VibeMaker;
using Ymir.VibeMaker.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ymir")
    ?? throw new InvalidOperationException("Connection string 'ymir' is not configured.");

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddYmirAuth(builder.Environment);
builder.Services.AddPublicEdge(builder.Configuration, builder.Environment);
builder.Services.AddPlatformInfrastructure(connectionString);
builder.Services.AddVibeMakerApplication();
builder.Services.AddVibeMakerInfrastructure(builder.Configuration, connectionString, builder.Environment.IsDevelopment());

var app = builder.Build();

// 經由 Cloudflare Tunnel 對外時必須最先執行，後面才會看到正確的 scheme 與用戶端 IP（ADR-0006）。
app.UsePublicEdge();

if (app.Environment.IsDevelopment())
{
    await DatabaseMigrator.MigrateAsync(app.Services);
}

// 錯誤回應不得暴露 stack trace / host path（SA §12），一律使用 ProblemDetails。
app.UseExceptionHandler();
app.UseStatusCodePages();

// 正式部署由 API 提供 Angular build（同源，ADR-0002）；開發期由 ng serve 提供。
app.UseYmirWebApp();

app.UseAuthentication();
app.UseAuthorization();
app.UseXsrfTokenCookie();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapAuthEndpoints(app.Environment);
app.MapProjectEndpoints();
app.MapConversationEndpoints();
app.MapExecutionEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();

/// <summary>讓整合測試可以使用 <c>WebApplicationFactory&lt;Program&gt;</c>。</summary>
public partial class Program;
