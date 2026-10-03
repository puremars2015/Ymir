using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ymir.Testing.FakeLlm;

/// <summary>
/// 在本機隨機 port 啟動 Fake LLM，供整合測試與 PoC 使用。
/// Pi 等外部程序需要真正的 HTTP 端點，所以不能只用 in-memory TestServer。
/// </summary>
public sealed class FakeLlmServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeLlmServer(WebApplication app, FakeLlmState state, Uri baseUrl)
    {
        _app = app;
        State = state;
        BaseUrl = baseUrl;
    }

    /// <summary>OpenAI 相容的 base URL，例如 <c>http://127.0.0.1:5123/v1</c>。</summary>
    public Uri BaseUrl { get; }

    public FakeLlmState State { get; }

    /// <param name="url">監聽位址；預設 <c>http://127.0.0.1:0</c>（隨機 port）。</param>
    public static async Task<FakeLlmServer> StartAsync(string url = "http://127.0.0.1:0", CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);
        var state = new FakeLlmState();
        builder.Services.AddSingleton(state);

        var app = builder.Build();
        app.MapFakeLlm();
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return new FakeLlmServer(app, state, new Uri($"{address.TrimEnd('/')}/v1"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
