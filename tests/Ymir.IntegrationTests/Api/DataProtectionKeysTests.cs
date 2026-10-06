using Microsoft.AspNetCore.Hosting;

namespace Ymir.IntegrationTests.Api;

/// <summary>設定 Ymir:DataProtection:KeysPath 時，金鑰寫到該目錄（API 在容器內時掛載主機目錄，ADR-0008）。</summary>
public sealed class DataProtectionKeysApiFactory : ApiFactory
{
    public string KeysPath { get; } = Path.Combine(Path.GetTempPath(), "ymir-dp-it-" + Guid.NewGuid().ToString("N"));

    protected override void Configure(IWebHostBuilder builder) => builder.UseSetting("Ymir:DataProtection:KeysPath", KeysPath);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(KeysPath))
        {
            Directory.Delete(KeysPath, recursive: true);
        }
    }
}

public class DataProtectionKeysTests(DataProtectionKeysApiFactory factory) : IClassFixture<DataProtectionKeysApiFactory>
{
    [Fact]
    public async Task Login_PersistsKeysToConfiguredDirectory()
    {
        using var client = await factory.LoginAsync("dp-user");

        Assert.NotEmpty(Directory.GetFiles(factory.KeysPath, "key-*.xml"));
    }
}
