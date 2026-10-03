using Ymir.Platform.Users;

namespace Ymir.Platform.Identity;

/// <summary>
/// 目前請求的使用者。所有擁有者檢查都必須以此為準，不得信任前端傳入的 user id（SA §12）。
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Ymir 使用者 id；未登入時存取會拋出 <see cref="InvalidOperationException"/>。</summary>
    Guid UserId { get; }

    UserRole Role { get; }

    /// <summary>稽核紀錄用的 actor 名稱。</summary>
    string ActorName { get; }
}
