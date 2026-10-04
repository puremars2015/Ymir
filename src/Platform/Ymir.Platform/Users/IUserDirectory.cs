using Ymir.Platform.Identity;

namespace Ymir.Platform.Users;

/// <summary>使用者資料存取（其他模組只透過此介面取得使用者，不直接存取 platform schema）。</summary>
public interface IUserDirectory
{
    /// <summary>登入時建立或更新使用者（SA §4.1）。新使用者的角色為 <paramref name="roleForNewUser"/>。</summary>
    Task<User> UpsertOnLoginAsync(ExternalIdentity identity, UserRole roleForNewUser, CancellationToken cancellationToken);

    Task<User?> FindAsync(Guid userId, CancellationToken cancellationToken);
}
