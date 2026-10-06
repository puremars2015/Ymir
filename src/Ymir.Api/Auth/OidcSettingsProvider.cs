using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Ymir.Platform.Settings;

namespace Ymir.Api.Auth;

/// <summary>目前生效的 OIDC 設定（同步讀取，供 options 設定與 claims 對應使用）。</summary>
public interface IOidcSettingsSource
{
    EffectiveOidcSettings Current { get; }
}

/// <summary>
/// 企業帳號登入設定的來源（ADR-0010）：資料庫（管理介面）優先，否則用部署設定。
/// <para>
/// 不重啟就生效：<c>oidc</c> scheme 依目前設定動態加入 / 移除（<see cref="IAuthenticationSchemeProvider"/>），
/// 並清掉 <see cref="OpenIdConnectOptions"/> 的快取；下一個登入請求就用新設定重建 handler 與 metadata。
/// 未設定時不註冊 scheme，避免 OpenIdConnectOptions 驗證失敗影響其他請求。
/// </para>
/// <para>多個 API 實例時，其他實例在 <see cref="RefreshInterval"/> 內讀到新設定。</para>
/// </summary>
public sealed partial class OidcSettingsProvider(
    IServiceScopeFactory scopeFactory,
    IOptions<YmirAuthOptions> deployment,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
    TimeProvider timeProvider,
    ILogger<OidcSettingsProvider> logger) : IOidcSettingsSource, IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile EffectiveOidcSettings _current = EffectiveOidcSettings.FromDeployment(deployment.Value.Oidc);
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private bool _schemeRegistered;

    public EffectiveOidcSettings Current => _current;

    /// <summary>超過 <see cref="RefreshInterval"/> 才重新讀取資料庫。</summary>
    public async Task<EffectiveOidcSettings> GetAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() - _loadedAt >= RefreshInterval)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return _current;
    }

    /// <summary>重新讀取資料庫並套用（管理介面存檔後呼叫）。</summary>
    public async Task<EffectiveOidcSettings> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var changed = next != _current;
            _current = next;
            _loadedAt = timeProvider.GetUtcNow();
            if (changed || _schemeRegistered != next.IsConfigured)
            {
                ApplyScheme(next);
            }

            return next;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<EffectiveOidcSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var fromDeployment = EffectiveOidcSettings.FromDeployment(deployment.Value.Oidc);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>();
                var stored = await store.GetAsync(OidcSettingsKeys.Document, cancellationToken).ConfigureAwait(false);
                var document = stored is null ? null : OidcSettingsDocument.FromJson(stored.Value);
                if (document is null)
                {
                    return fromDeployment;
                }

                var secret = await store.GetSecretAsync(OidcSettingsKeys.ClientSecret, cancellationToken).ConfigureAwait(false);
                var authorityHost = deployment.Value.Oidc.AuthorityHost;
                return new EffectiveOidcSettings(
                    OidcSettingsSource.Database,
                    document.Enabled,
                    document.TenantId,
                    OidcSettingsKeys.BuildAuthority(authorityHost, document.TenantId),
                    document.ClientId,
                    secret?.Value ?? deployment.Value.Oidc.ClientSecret,
                    secret is not null ? OidcSettingsSource.Database
                        : string.IsNullOrWhiteSpace(deployment.Value.Oidc.ClientSecret) ? OidcSettingsSource.None : OidcSettingsSource.Deployment,
                    secret?.UpdatedAt,
                    document.SecretExpiresOn,
                    document.AdminRole,
                    deployment.Value.Oidc.SubjectClaim,
                    document.DisplayName,
                    stored!.UpdatedAt,
                    stored.UpdatedBy);
            }
        }
#pragma warning disable CA1031 // 資料庫暫時無法使用（或正式環境尚未 migrate）時退回部署設定，不讓登入整個失效。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogLoadFailed(logger, ex);
            return fromDeployment;
        }
    }

    public void Dispose() => _refreshLock.Dispose();

    private void ApplyScheme(EffectiveOidcSettings settings)
    {
        schemes.RemoveScheme(AuthSetup.OidcScheme);
        optionsCache.TryRemove(AuthSetup.OidcScheme);
        _schemeRegistered = false;
        if (settings.IsConfigured)
        {
            schemes.AddScheme(new AuthenticationScheme(AuthSetup.OidcScheme, null, typeof(OpenIdConnectHandler)));
            _schemeRegistered = true;
        }

        LogApplied(logger, settings.Source, settings.IsConfigured);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load OIDC settings from the database; using deployment settings")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC settings applied (source {Source}, enabled {Enabled})")]
    private static partial void LogApplied(ILogger logger, OidcSettingsSource source, bool enabled);
}

/// <summary><see cref="OpenIdConnectOptions"/> 由目前生效的設定產生（取代啟動時固定的設定）。</summary>
internal sealed class ConfigureOidcOptions(IOidcSettingsSource settings, IHostEnvironment environment) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name == AuthSetup.OidcScheme)
        {
            OidcSignIn.Configure(options, settings.Current, environment);
        }
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}

/// <summary>啟動時讀一次資料庫中的設定並註冊 scheme。</summary>
internal sealed class OidcSettingsWarmup(OidcSettingsProvider provider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => provider.RefreshAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
