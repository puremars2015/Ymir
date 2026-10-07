using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Connectors.OneDrive;

public enum OneDriveLinkState
{
    NotConnected,
    Connected,
    NeedsReauth,
}

/// <summary>給使用者看的連結狀態；不含任何 token（ADR-0013 §2）。</summary>
/// <param name="Allowed">管理員是否開放（擴充政策的 <c>onedrive</c> 能力）。</param>
/// <param name="Available">是否設定了企業帳號登入（Entra）；沒有時無法連結。</param>
public sealed record OneDriveStatus(
    bool Allowed,
    bool Available,
    OneDriveLinkState State,
    string? Account,
    string? RootPath,
    DateTimeOffset? ConnectedAt,
    string? LastError);

public enum OneDriveConnectResult
{
    Connected,

    /// <summary>企業帳號使用者連到別人的 Microsoft 帳號（oid 不同），拒絕。</summary>
    AccountMismatch,

    Failed,
}

/// <summary>access token 只放記憶體（ADR-0013 §2）；服務重啟後以 refresh token 重新換發。</summary>
public sealed class OneDriveAccessTokenCache
{
    private readonly ConcurrentDictionary<Guid, (string Token, DateTimeOffset ExpiresAt)> _tokens = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    internal bool TryGet(Guid userId, DateTimeOffset now, out string token)
    {
        // 提早 2 分鐘換發，避免長時間的同步途中過期。
        if (_tokens.TryGetValue(userId, out var entry) && entry.ExpiresAt - TimeSpan.FromMinutes(2) > now)
        {
            token = entry.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    internal void Set(Guid userId, string token, DateTimeOffset expiresAt) => _tokens[userId] = (token, expiresAt);

    internal void Remove(Guid userId) => _tokens.TryRemove(userId, out _);

    /// <summary>同一使用者的換發要序列化：refresh token 會輪替，並行換發會讓其中一個拿到失效的 token。</summary>
    internal SemaphoreSlim LockFor(Guid userId) => _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
}

/// <summary>
/// OneDrive 連結（ADR-0013）：完成授權碼交換、保存加密的 refresh token、設定根資料夾、解除連結，
/// 以及為同步取得 access token。所有操作只作用在呼叫端給的 user id（由 Api 層從目前使用者取得，SA §12）。
/// </summary>
public sealed partial class OneDriveConnectionService(
    IVibeMakerDbContext db,
    IExtensionPolicy extensionPolicy,
    IOneDriveOAuthSettingsSource oauthSettings,
    IOneDriveOAuthClient oauth,
    IOneDriveClient graph,
    IOneDriveTokenProtector protector,
    OneDriveAccessTokenCache tokenCache,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    ILogger<OneDriveConnectionService> logger)
{
    public async Task<OneDriveStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var allowed = (await extensionPolicy.ResolveAsync(userId, cancellationToken).ConfigureAwait(false)).OneDrive;
        var available = await oauthSettings.GetAsync(cancellationToken).ConfigureAwait(false) is not null;
        var connection = await db.OneDriveConnections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        return connection is null
            ? new OneDriveStatus(allowed, available, OneDriveLinkState.NotConnected, null, null, null, null)
            : new OneDriveStatus(
                allowed,
                available,
                connection.Status == OneDriveConnectionStatus.Connected ? OneDriveLinkState.Connected : OneDriveLinkState.NeedsReauth,
                connection.UserPrincipalName,
                connection.RootPath,
                connection.ConnectedAt,
                connection.LastError);
    }

    public async Task<bool> IsAllowedAsync(Guid userId, CancellationToken cancellationToken) =>
        (await extensionPolicy.ResolveAsync(userId, cancellationToken).ConfigureAwait(false)).OneDrive;

    /// <summary>導向 Microsoft 的授權網址；沒有設定 Entra 時回傳 null。</summary>
    public async Task<Uri?> BuildAuthorizeUriAsync(Uri redirectUri, string state, string codeChallenge, string? loginHint, CancellationToken cancellationToken) =>
        await oauthSettings.GetAsync(cancellationToken).ConfigureAwait(false) is { } settings
            ? oauth.BuildAuthorizeUri(settings, redirectUri, state, codeChallenge, loginHint)
            : null;

    /// <param name="expectedMicrosoftUserId">企業帳號使用者的 oid：連結的 Microsoft 帳號必須是同一人；本機帳號為 null。</param>
    public async Task<OneDriveConnectResult> CompleteConnectAsync(
        Guid userId,
        string? expectedMicrosoftUserId,
        string code,
        Uri redirectUri,
        string codeVerifier,
        string actor,
        CancellationToken cancellationToken)
    {
        var settings = await oauthSettings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            return OneDriveConnectResult.Failed;
        }

        try
        {
            var tokens = await oauth.RedeemCodeAsync(settings, code, redirectUri, codeVerifier, cancellationToken).ConfigureAwait(false);
            if (tokens.RefreshToken is null)
            {
                LogNoRefreshToken(logger, userId);
                return OneDriveConnectResult.Failed;
            }

            var me = await graph.GetMeAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
            if (expectedMicrosoftUserId is not null && !string.Equals(me.Id, expectedMicrosoftUserId, StringComparison.OrdinalIgnoreCase))
            {
                await AuditAsync(actor, "connector.onedrive.connect", userId, AuditResult.Failure, cancellationToken).ConfigureAwait(false);
                return OneDriveConnectResult.AccountMismatch;
            }

            var driveId = await graph.GetDriveIdAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
            var now = timeProvider.GetUtcNow();
            var protectedToken = protector.Protect(tokens.RefreshToken);
            var connection = await db.OneDriveConnections.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                db.OneDriveConnections.Add(OneDriveConnection.Create(userId, me.Id, me.UserPrincipalName, driveId, protectedToken, now));
            }
            else
            {
                connection.Reconnect(me.Id, me.UserPrincipalName, driveId, protectedToken, now);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            tokenCache.Set(userId, tokens.AccessToken, tokens.ExpiresAt);
            await AuditAsync(actor, "connector.onedrive.connect", userId, AuditResult.Success, cancellationToken).ConfigureAwait(false);
            return OneDriveConnectResult.Connected;
        }
        catch (OneDriveException ex)
        {
            LogConnectFailed(logger, ex, userId);
            await AuditAsync(actor, "connector.onedrive.connect", userId, AuditResult.Failure, cancellationToken).ConfigureAwait(false);
            return OneDriveConnectResult.Failed;
        }
    }

    /// <summary>設定根資料夾：在使用者自己的 drive 逐層取得或建立（ADR-0013 §3）。回傳給使用者看的錯誤，成功時為 null。</summary>
    public async Task<string?> SetRootAsync(Guid userId, string? path, string actor, CancellationToken cancellationToken)
    {
        var (normalized, problem) = OneDrivePaths.NormalizeRoot(path);
        if (normalized is null)
        {
            return problem;
        }

        var connection = await db.OneDriveConnections.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return "尚未連結 OneDrive。";
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(userId, cancellationToken).ConfigureAwait(false);
            var folder = await graph.GetRootAsync(accessToken, cancellationToken).ConfigureAwait(false);
            foreach (var segment in OneDrivePaths.Segments(normalized))
            {
                folder = await graph.EnsureFolderAsync(accessToken, folder.Id, segment, cancellationToken).ConfigureAwait(false);
            }

            await db.Entry(connection).ReloadAsync(cancellationToken).ConfigureAwait(false);
            connection.SetRoot(folder.Id, normalized, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await AuditAsync(actor, "connector.onedrive.root.update", userId, AuditResult.Success, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OneDriveAuthorizationException)
        {
            return "OneDrive 授權已失效，請重新連結。";
        }
        catch (OneDriveException ex)
        {
            LogRootFailed(logger, ex, userId);
            return ex.Message;
        }
    }

    public async Task<bool> DisconnectAsync(Guid userId, string actor, CancellationToken cancellationToken)
    {
        var connection = await db.OneDriveConnections.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        tokenCache.Remove(userId);
        if (connection is null)
        {
            return false;
        }

        // 只刪除連結與 token；雲端與本機的檔案都保留（ADR-0013 §2）。
        db.OneDriveConnections.Remove(connection);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(actor, "connector.onedrive.disconnect", userId, AuditResult.Success, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 取得可用的 access token：快取有效就直接用，否則以 refresh token 換發並保存輪替後的 refresh token。
    /// refresh token 失效時把連結標記為 <see cref="OneDriveConnectionStatus.NeedsReauth"/> 並拋出 <see cref="OneDriveAuthorizationException"/>。
    /// </summary>
    public async Task<string> GetAccessTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (tokenCache.TryGet(userId, timeProvider.GetUtcNow(), out var cached))
        {
            return cached;
        }

        var userLock = tokenCache.LockFor(userId);
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tokenCache.TryGet(userId, timeProvider.GetUtcNow(), out cached))
            {
                return cached;
            }

            var connection = await db.OneDriveConnections.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false)
                ?? throw new OneDriveAuthorizationException("尚未連結 OneDrive。");
            if (connection.Status != OneDriveConnectionStatus.Connected)
            {
                throw new OneDriveAuthorizationException("OneDrive 授權已失效，請重新連結。");
            }

            var settings = await oauthSettings.GetAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new OneDriveException("企業帳號登入尚未設定，無法連線 OneDrive。");
            var refreshToken = protector.Unprotect(connection.ProtectedRefreshToken);
            try
            {
                if (refreshToken is null)
                {
                    throw new OneDriveAuthorizationException("無法讀取 OneDrive 授權，請重新連結。");
                }

                var tokens = await oauth.RefreshAsync(settings, refreshToken, cancellationToken).ConfigureAwait(false);
                if (tokens.RefreshToken is { } rotated)
                {
                    connection.RotateRefreshToken(protector.Protect(rotated), timeProvider.GetUtcNow());
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                tokenCache.Set(userId, tokens.AccessToken, tokens.ExpiresAt);
                return tokens.AccessToken;
            }
            catch (OneDriveAuthorizationException ex)
            {
                connection.MarkNeedsReauth(ex.Message, timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                LogNeedsReauth(logger, userId);
                throw;
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    private Task AuditAsync(string actor, string action, Guid userId, AuditResult result, CancellationToken cancellationToken) =>
        auditLog.WriteAsync(new AuditEntry(actor, action, "user", userId.ToString("D"), result, timeProvider.GetUtcNow(), null), cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OneDrive authorization for user {UserId} returned no refresh token (offline_access not granted?)")]
    private static partial void LogNoRefreshToken(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connecting OneDrive failed for user {UserId}")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Setting the OneDrive root folder failed for user {UserId}")]
    private static partial void LogRootFailed(ILogger logger, Exception exception, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OneDrive refresh token for user {UserId} is no longer valid; the user must reconnect")]
    private static partial void LogNeedsReauth(ILogger logger, Guid userId);
}
