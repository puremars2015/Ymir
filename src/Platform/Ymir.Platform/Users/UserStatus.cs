namespace Ymir.Platform.Users;

public enum UserStatus
{
    Active = 0,

    /// <summary>不得再建立 execution；其 active runtime 應由管理流程停止（SA §12）。</summary>
    Disabled = 1,
}
