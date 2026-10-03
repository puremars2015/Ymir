using System.Text.Json.Serialization;
using Ymir.Api.Auth;
using Ymir.Api.Endpoints;
using Ymir.Api.Infrastructure;
using Ymir.Platform.Infrastructure;
using Ymir.VibeMaker.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ymir")
    ?? throw new InvalidOperationException("Connection string 'ymir' is not configured.");

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddYmirAuth(builder.Environment);
builder.Services.AddPlatformInfrastructure(connectionString);
builder.Services.AddVibeMakerInfrastructure(builder.Configuration, connectionString, builder.Environment.IsDevelopment());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await DatabaseMigrator.MigrateAsync(app.Services);
}

// 錯誤回應不得暴露 stack trace / host path（SA §12），一律使用 ProblemDetails。
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseXsrfTokenCookie();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapDevAgentEndpoints();
}

app.MapAuthEndpoints(app.Environment);
app.MapDefaultEndpoints();

await app.RunAsync();

/// <summary>讓整合測試可以使用 <c>WebApplicationFactory&lt;Program&gt;</c>。</summary>
public partial class Program;
