using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ymir.Api.Auth;

/// <summary>設定來源（ADR-0010）：資料庫（管理介面）優先於部署設定（<c>.env</c>）。</summary>
public enum OidcSettingsSource
{
    None,
    Deployment,
    Database,
}

/// <summary>
/// 目前生效的企業帳號（Entra ID）登入設定（ADR-0009、ADR-0010）。
/// <see cref="ClientSecret"/> 只在伺服器內使用，任何 API 回應都不得包含。
/// </summary>
public sealed record EffectiveOidcSettings(
    OidcSettingsSource Source,
    bool Enabled,
    string? TenantId,
    string? Authority,
    string? ClientId,
    string? ClientSecret,
    OidcSettingsSource SecretSource,
    DateTimeOffset? SecretUpdatedAt,
    DateOnly? SecretExpiresOn,
    string AdminRole,
    string SubjectClaim,
    string DisplayName,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy)
{
    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static EffectiveOidcSettings FromDeployment(OidcLoginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.IsConfigured;
        return new(
            configured ? OidcSettingsSource.Deployment : OidcSettingsSource.None,
            configured,
            TenantFromAuthority(options.Authority),
            options.Authority,
            options.ClientId,
            options.ClientSecret,
            string.IsNullOrWhiteSpace(options.ClientSecret) ? OidcSettingsSource.None : OidcSettingsSource.Deployment,
            null,
            null,
            options.AdminRole,
            options.SubjectClaim,
            options.DisplayName,
            null,
            null);
    }

    /// <summary>從 <c>https://login.microsoftonline.com/{tenant}/v2.0</c> 取出 tenant（只為了在管理介面顯示）。</summary>
    internal static string? TenantFromAuthority(string? authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.Segments.Length >= 2 && Guid.TryParse(uri.Segments[1].TrimEnd('/'), out var tenant)
            ? tenant.ToString("D")
            : null;
}

/// <summary>管理介面存在資料庫的 Entra 設定（JSON，key <see cref="OidcSettingsKeys.Document"/>）；client secret 另外加密存放。</summary>
public sealed record OidcSettingsDocument(
    bool Enabled,
    string TenantId,
    string ClientId,
    string AdminRole,
    string DisplayName,
    DateOnly? SecretExpiresOn)
{
    public string ToJson() => JsonSerializer.Serialize(this, OidcSettingsKeys.Json);

    public static OidcSettingsDocument? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OidcSettingsDocument>(json, OidcSettingsKeys.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static partial class OidcSettingsKeys
{
    public const string Document = "auth.oidc";
    public const string ClientSecret = "auth.oidc.client_secret";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Authority 由固定的 host 與 tenant id 組成，管理員無法填入任意網址（避免 SSRF，ADR-0010）。</summary>
    public static string BuildAuthority(string authorityHost, string tenantId) =>
        $"{authorityHost.TrimEnd('/')}/{tenantId}/v2.0";

    /// <summary>驗證管理介面送來的設定；回傳錯誤訊息（null 表示通過）。</summary>
    public static string? Validate(string? tenantId, string? clientId, string? adminRole, string? displayName, string? clientSecret)
    {
        if (!Guid.TryParse(tenantId, out _))
        {
            return "Tenant ID 必須是 GUID（目錄識別碼）。";
        }

        if (string.IsNullOrWhiteSpace(clientId) || !IdentifierPattern().IsMatch(clientId))
        {
            return "Client ID 格式不正確。";
        }

        if (string.IsNullOrWhiteSpace(adminRole) || adminRole.Length > 100 || adminRole.Any(char.IsWhiteSpace))
        {
            return "Admin 角色必須是 1～100 個字元，不可含空白。";
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 30)
        {
            return "登入按鈕名稱必須是 1～30 個字。";
        }

        if (clientSecret is not null && (clientSecret.Length is 0 or > 500 || clientSecret.Any(char.IsControl)))
        {
            return "Client secret 格式不正確。";
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex IdentifierPattern();
}
