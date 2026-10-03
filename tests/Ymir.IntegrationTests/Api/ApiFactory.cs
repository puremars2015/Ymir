using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ymir.IntegrationTests.Api;

/// <summary>
/// Development 環境 + 獨立的測試資料庫（啟動時自動 migrate）+ Scripted harness + Local runtime。
/// 可透過 <see cref="Configure"/> 覆寫設定（例如改用真正的 Pi）。
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TestDatabase _database = new();

    public string WorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "ymir-it-" + Guid.NewGuid().ToString("N"));

    internal string DatabaseConnectionString => _database.ConnectionString;

    protected virtual void Configure(IWebHostBuilder builder)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ymir", _database.ConnectionString);
        builder.UseSetting("VibeMaker:Harness", "Scripted");
        builder.UseSetting("VibeMaker:Runtime:Provider", "Local");
        builder.UseSetting("VibeMaker:Runtime:WorkspaceRoot", WorkspaceRoot);
        Configure(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
