using Microsoft.AspNetCore.Hosting;
using Ymir.IntegrationTests.Api;
using Ymir.IntegrationTests.PiAgent;
using Ymir.Testing.FakeLlm;

namespace Ymir.IntegrationTests.Make;

/// <summary>Windows 的 Pi 在 Docker 內執行，必須經 host.docker.internal 連回測試模型。</summary>
public sealed class MakeApiFactory : ApiFactory
{
    private readonly Lazy<FakeLlmServer> _fakeLlm = new(() =>
        FakeLlmServer.StartAsync(OperatingSystem.IsWindows() ? "http://0.0.0.0:0" : "http://127.0.0.1:0").GetAwaiter().GetResult());

    protected override void Configure(IWebHostBuilder builder)
    {
        var baseUrl = _fakeLlm.Value.BaseUrl;
        if (OperatingSystem.IsWindows())
        {
            baseUrl = new UriBuilder(baseUrl) { Host = "host.docker.internal" }.Uri;
        }

        builder.UseSetting("VibeMaker:Harness", "Pi");
        builder.UseSetting("VibeMaker:Pi:ModelBaseUrl", baseUrl.ToString());
        builder.UseSetting("VibeMaker:Pi:ModelId", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Pi:DevelopmentApiKey", "integration-test-key");
        builder.UseSetting("VibeMaker:Pi:AutoRetry", "false");
        builder.UseSetting("VibeMaker:Models:0:Id", FakeLlmEndpoints.ModelId);
        builder.UseSetting("VibeMaker:Models:1:Id", PiHarnessFixture.SecondModelId);
    }

    public FakeLlmState FakeLlm => _fakeLlm.Value.State;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_fakeLlm.IsValueCreated)
        {
            await _fakeLlm.Value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
