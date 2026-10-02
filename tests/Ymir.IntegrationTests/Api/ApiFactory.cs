using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ymir.IntegrationTests.Api;

/// <summary>Development 環境 + Scripted harness + Local runtime，不需要 Pi / Podman / LLM。</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "ymir-it-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("VibeMaker:Harness", "Scripted");
        builder.UseSetting("VibeMaker:Runtime:Provider", "Local");
        builder.UseSetting("VibeMaker:Runtime:WorkspaceRoot", _workspaceRoot);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }
}
