namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

/// <summary>
/// 連結 OneDrive 用的 Entra 應用程式設定（ADR-0013 §1）：沿用 Ymir 登入的註冊（ADR-0010 的 <c>OidcSettingsProvider</c>），
/// 由 Api 層提供；client secret 只在伺服器內使用。
/// </summary>
/// <param name="Authority">Entra v2 authority，例如 <c>https://login.microsoftonline.com/{tenant}/v2.0</c>。</param>
public sealed record OneDriveOAuthSettings(string Authority, string ClientId, string ClientSecret)
{
    public const string Scope = "offline_access Files.ReadWrite User.Read";

    /// <summary><c>{authority 去掉 /v2.0}/oauth2/v2.0/{endpoint}</c>：Entra 與 Fake OIDC 的路徑配置相同。</summary>
    public Uri Endpoint(string name)
    {
        var baseUri = Authority.EndsWith("/v2.0", StringComparison.Ordinal) ? Authority[..^"/v2.0".Length] : Authority.TrimEnd('/');
        return new Uri($"{baseUri}/oauth2/v2.0/{name}");
    }

    /// <summary>record 預設的 ToString 會印出 secret。</summary>
    public override string ToString() => $"OneDriveOAuthSettings {{ Authority = {Authority}, ClientId = {ClientId} }}";
}

public interface IOneDriveOAuthSettingsSource
{
    /// <summary>沒有設定企業帳號登入（Entra）時回傳 null：無法連結 OneDrive。</summary>
    Task<OneDriveOAuthSettings?> GetAsync(CancellationToken cancellationToken);
}

/// <param name="RefreshToken">Entra 每次換發都會給新的 refresh token；沒有時沿用舊的。</param>
public sealed record OneDriveTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"OneDriveTokens {{ ExpiresAt = {ExpiresAt} }}";
}

/// <summary>授權碼 + PKCE 與 refresh token 換發（ADR-0013 §1、§2）。</summary>
public interface IOneDriveOAuthClient
{
    Uri BuildAuthorizeUri(OneDriveOAuthSettings settings, Uri redirectUri, string state, string codeChallenge, string? loginHint);

    Task<OneDriveTokens> RedeemCodeAsync(OneDriveOAuthSettings settings, string code, Uri redirectUri, string codeVerifier, CancellationToken cancellationToken);

    /// <summary>refresh token 已失效（<c>invalid_grant</c>）時拋出 <see cref="OneDriveAuthorizationException"/>。</summary>
    Task<OneDriveTokens> RefreshAsync(OneDriveOAuthSettings settings, string refreshToken, CancellationToken cancellationToken);
}

public sealed record GraphUser(string Id, string UserPrincipalName);

public sealed record DriveItemInfo(string Id, string Name, bool IsFolder, string? ETag, long Size, DateTimeOffset LastModified);

/// <summary>Microsoft Graph 的 OneDrive 操作（只在後端呼叫，Agent 拿不到 token，ADR-0013 §5）。</summary>
public interface IOneDriveClient
{
    Task<GraphUser> GetMeAsync(string accessToken, CancellationToken cancellationToken);

    Task<string> GetDriveIdAsync(string accessToken, CancellationToken cancellationToken);

    Task<DriveItemInfo> GetRootAsync(string accessToken, CancellationToken cancellationToken);

    Task<DriveItemInfo?> GetItemAsync(string accessToken, string itemId, CancellationToken cancellationToken);

    /// <summary>在 <paramref name="parentId"/> 底下取得或建立資料夾；同名的是檔案時拋出 <see cref="OneDriveException"/>。</summary>
    Task<DriveItemInfo> EnsureFolderAsync(string accessToken, string parentId, string name, CancellationToken cancellationToken);
}

/// <summary>refresh token 的加解密（Data Protection，purpose <c>Ymir.Connectors.OneDrive.v1</c>，ADR-0013 §2）。</summary>
public interface IOneDriveTokenProtector
{
    string Protect(string refreshToken);

    /// <summary>金鑰遺失等無法解密時回傳 null（視同需要重新連結）。</summary>
    string? Unprotect(string protectedRefreshToken);
}

/// <summary>Graph 或授權呼叫失敗；<see cref="Exception.Message"/> 是可以給使用者看的摘要（不含 token、路徑內容）。</summary>
public class OneDriveException(string summary, Exception? inner = null) : Exception(summary, inner);

/// <summary>授權已失效，需要使用者重新連結。</summary>
public sealed class OneDriveAuthorizationException(string summary, Exception? inner = null) : OneDriveException(summary, inner);
