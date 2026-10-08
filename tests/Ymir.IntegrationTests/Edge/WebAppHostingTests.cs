using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ymir.Edge;

namespace Ymir.IntegrationTests.Edge;

/// <summary>API 提供 Angular build（同源部署，ADR-0006）：前端路由回 index.html、/api 不被吃掉、預設拒絕的授權不受影響。</summary>
public sealed class WebAppHostingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ymir-web-").FullName;

    public WebAppHostingTests()
    {
        File.WriteAllText(Path.Combine(_root, "index.html"), "<app-root></app-root>");
        File.WriteAllText(Path.Combine(_root, "main.js"), "console.log('ymir');");
        File.WriteAllText(Path.Combine(_root, "sw.js"), "// sw");
        File.WriteAllText(Path.Combine(_root, "manifest.webmanifest"), "{}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static async Task<WebApplication> StartAsync(string? rootPath)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        if (rootPath is not null)
        {
            builder.Configuration[WebAppHostingExtensions.RootPathKey] = rootPath;
        }

        // 與 Ymir.Api 相同：預設要求登入（fallback policy）。
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            });
        builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var app = builder.Build();
        app.UseYmirWebApp();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/ping", () => "pong");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/c/6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public async Task FrontendRoutes_ServeIndexHtml_Anonymously(string path)
    {
        await using var app = await StartAsync(_root);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<app-root></app-root>", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaticAssets_AreServed_AndMissingAssetsAre404()
    {
        await using var app = await StartAsync(_root);
        using var client = app.GetTestClient();

        using var asset = await client.GetAsync("/main.js", TestContext.Current.CancellationToken);
        using var missing = await client.GetAsync("/missing.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, missing.StatusCode);
    }

    [Theory]
    [InlineData("/sw.js")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/")]
    public async Task PwaEntryFiles_RequireRevalidation_OtherAssetsDoNot(string path)
    {
        await using var app = await StartAsync(_root);
        using var client = app.GetTestClient();

        using var entry = await client.GetAsync(path, TestContext.Current.CancellationToken);
        using var asset = await client.GetAsync("/main.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Equal("no-cache", entry.Headers.CacheControl?.ToString());
        Assert.NotEqual("no-cache", asset.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task WebManifest_IsServedAsManifestJson()
    {
        await using var app = await StartAsync(_root);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/manifest.webmanifest", TestContext.Current.CancellationToken);

        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/ping")]
    [InlineData("/api/unknown")]
    public async Task ApiRoutes_AreNotServedAsHtml_AndStillRequireLogin(string path)
    {
        await using var app = await StartAsync(_root);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NotConfigured_ServesNothing()
    {
        await using var app = await StartAsync(rootPath: null);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RootPathWithoutIndexHtml_RefusesToStart()
    {
        var empty = Directory.CreateTempSubdirectory("ymir-web-empty-").FullName;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(empty));
        }
        finally
        {
            Directory.Delete(empty);
        }
    }
}
