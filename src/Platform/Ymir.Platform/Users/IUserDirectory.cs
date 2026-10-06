using Ymir.Platform.Identity;

namespace Ymir.Platform.Users;

/// <summary>使用者資料存取（其他模組只透過此介面取得使用者，不直接存取 platform schema）。</summary>
public interface IUserDirectory
{
    /// <summary>
    /// 登入時建立或更新使用者（SA §4.1）。新使用者的角色為 <paramref name="role"/>；
    /// <paramref name="roleManagedByIdentityProvider"/> 為 true 時（Entra ID app role，ADR-0009）既有使用者的角色也同步為 <paramref name="role"/>。
    /// </summary>
    Task<User> UpsertOnLoginAsync(ExternalIdentity identity, UserRole role, CancellationToken cancellationToken, bool roleManagedByIdentityProvider = false);

    Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>管理用列表（Admin，SA §4）：依名稱 / 帳號 / email 搜尋，依最後登入時間排序。</summary>
    Task<IReadOnlyList<User>> ListAsync(string? search, int take, CancellationToken cancellationToken);

    /// <summary>依 id 批次取得使用者（稽核頁、總覽顯示名稱用）；找不到的 id 不會出現在結果中。</summary>
    Task<IReadOnlyList<User>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>是否還有啟用中的本機 Admin（停用企業帳號登入前的防鎖死檢查，ADR-0010）。</summary>
    Task<bool> HasActiveLocalAdminAsync(CancellationToken cancellationToken);

    /// <summary>總覽統計（Admin 總覽，ADR-0010）。</summary>
    Task<UserStatistics> GetStatisticsAsync(DateTimeOffset activeSince, CancellationToken cancellationToken);

    /// <summary>停用 / 啟用；找不到使用者時回傳 null。</summary>
    Task<User?> SetStatusAsync(Guid userId, UserStatus status, CancellationToken cancellationToken);
}

/// <param name="ActiveSince">在 activeSince 之後登入過的使用者數。</param>
public sealed record UserStatistics(int Total, int Active, int Disabled, int Admins, int ActiveSince);
