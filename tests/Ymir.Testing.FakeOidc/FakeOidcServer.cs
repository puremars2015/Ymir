using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ymir.Testing.FakeOidc;

/// <summary>
/// 模擬 Entra ID 的本機 OIDC 伺服器（ADR-0009）：雲端沙箱與 CI 連不到 login.microsoftonline.com，
/// 用它驗證 Ymir 的 OIDC 登入流程（Authorization Code + PKCE、client secret、id_token 簽章、nonce、state）。
/// id_token 的 claims 與 Entra v2 相同：<c>iss</c>（含 tenant）、<c>tid</c>、<c>oid</c>、<c>sub</c>（pairwise）、
/// <c>name</c>、<c>preferred_username</c>、<c>email</c>、<c>roles</c>。
/// </summary>
public sealed class FakeOidcServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeOidcServer(WebApplication app, FakeOidcSettings settings, Uri rootUrl)
    {
        _app = app;
        Settings = settings;
        RootUrl = rootUrl;
    }

    public FakeOidcSettings Settings { get; }

    public Uri RootUrl { get; }

    /// <summary>給 Ymir 設定的 authority，格式同 Entra：<c>{root}/{tenant}/v2.0</c>。</summary>
    public string Authority => $"{RootUrl.ToString().TrimEnd('/')}/{Settings.TenantId}/v2.0";

    public static async Task<FakeOidcServer> StartAsync(FakeOidcSettings settings, string url = "http://127.0.0.1:0", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<FakeOidcIssuer>();

        var app = builder.Build();
        app.MapFakeOidc();
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return new FakeOidcServer(app, settings, new Uri(address.TrimEnd('/') + "/"));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}

/// <param name="TenantId">模擬的 Entra tenant。</param>
/// <param name="UserDomain">帳號的網域，<c>preferred_username</c> 為 <c>{帳號}@{網域}</c>。</param>
public sealed record FakeOidcSettings(
    string ClientId,
    string ClientSecret,
    string TenantId = "00000000-0000-0000-0000-00000000f00d",
    string UserDomain = "fake-entra.test",
    string AdminRole = "Ymir.Admin");
