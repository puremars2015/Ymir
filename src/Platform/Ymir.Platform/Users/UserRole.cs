namespace Ymir.Platform.Users;

/// <summary>SA §4 角色；<see cref="Guest"/> 見 ADR-0027。</summary>
public enum UserRole
{
    User = 0,
    Admin = 1,

    /// <summary>訪客（ADR-0027）：與 <see cref="User"/> 相同，但不能使用使用者之間的訊息與群組聊天。</summary>
    Guest = 2,
}
