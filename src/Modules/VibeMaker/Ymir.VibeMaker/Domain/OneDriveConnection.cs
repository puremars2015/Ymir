namespace Ymir.VibeMaker.Domain;

public enum OneDriveConnectionStatus
{
    Connected,

    /// <summary>refresh token 失效（密碼變更、權限撤回）：停止同步，待使用者重新連結（ADR-0013 §2）。</summary>
    NeedsReauth,
}

/// <summary>
/// 使用者連結的 OneDrive（ADR-0013）。一位使用者最多一個連結；跨模組只存 user id、不建 FK（ADR-0001）。
/// <see cref="ProtectedRefreshToken"/> 是 Data Protection 加密後的值，只在後端使用，不得出現在回應、log 或稽核。
/// </summary>
public sealed class OneDriveConnection
{
    public const int MicrosoftUserIdMaxLength = 64;
    public const int UserPrincipalNameMaxLength = 256;
    public const int DriveIdMaxLength = 200;
    public const int ItemIdMaxLength = 200;
    public const int RootPathMaxLength = 400;
    public const int ErrorMaxLength = 300;

    private OneDriveConnection()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Graph <c>/me</c> 的 id（Entra oid）。</summary>
    public string MicrosoftUserId { get; private set; } = string.Empty;

    public string UserPrincipalName { get; private set; } = string.Empty;

    public string DriveId { get; private set; } = string.Empty;

    /// <summary>使用者選定的根資料夾（ADR-0013 §3）；尚未設定時為 null，不同步。</summary>
    public string? RootItemId { get; private set; }

    public string? RootPath { get; private set; }

    public string ProtectedRefreshToken { get; private set; } = string.Empty;

    public OneDriveConnectionStatus Status { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static OneDriveConnection Create(Guid userId, string microsoftUserId, string userPrincipalName, string driveId, string protectedRefreshToken, DateTimeOffset now)
    {
        var connection = new OneDriveConnection { UserId = userId, ConnectedAt = now };
        connection.Reconnect(microsoftUserId, userPrincipalName, driveId, protectedRefreshToken, now);
        return connection;
    }

    /// <summary>重新連結：同一個 drive 時保留根資料夾；換了帳號或 drive 則清除，避免寫到別人的資料夾。</summary>
    public void Reconnect(string microsoftUserId, string userPrincipalName, string driveId, string protectedRefreshToken, DateTimeOffset now)
    {
        var sameDrive = DriveId == driveId;
        MicrosoftUserId = DomainGuard.RequiredText(microsoftUserId, MicrosoftUserIdMaxLength, nameof(microsoftUserId));
        UserPrincipalName = DomainGuard.RequiredText(userPrincipalName, UserPrincipalNameMaxLength, nameof(userPrincipalName));
        DriveId = DomainGuard.RequiredText(driveId, DriveIdMaxLength, nameof(driveId));
        ProtectedRefreshToken = DomainGuard.RequiredText(protectedRefreshToken, int.MaxValue, nameof(protectedRefreshToken));
        if (!sameDrive)
        {
            RootItemId = null;
            RootPath = null;
        }

        Status = OneDriveConnectionStatus.Connected;
        LastError = null;
        UpdatedAt = now;
    }

    public void SetRoot(string itemId, string path, DateTimeOffset now)
    {
        RootItemId = DomainGuard.RequiredText(itemId, ItemIdMaxLength, nameof(itemId));
        RootPath = DomainGuard.RequiredText(path, RootPathMaxLength, nameof(path));
        UpdatedAt = now;
    }

    /// <summary>refresh token 每次換發都會輪替（ADR-0013 §2）。</summary>
    public void RotateRefreshToken(string protectedRefreshToken, DateTimeOffset now)
    {
        ProtectedRefreshToken = DomainGuard.RequiredText(protectedRefreshToken, int.MaxValue, nameof(protectedRefreshToken));
        UpdatedAt = now;
    }

    public void MarkNeedsReauth(string error, DateTimeOffset now)
    {
        Status = OneDriveConnectionStatus.NeedsReauth;
        LastError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
        UpdatedAt = now;
    }
}
