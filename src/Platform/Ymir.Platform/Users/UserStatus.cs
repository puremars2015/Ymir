namespace Ymir.Platform.Users;

public enum UserStatus
{
    Active = 0,

    /// <summary>不得再建立 execution；其 active runtime 應由管理流程停止（SA §12）。</summary>
    Disabled = 1,

    /// <summary>
    /// Admin 刪除的帳號（ADR-0027）：資料列保留給歷史紀錄顯示名稱，外部身分已釋放（同一帳號可重新建立成新使用者），
    /// 不出現在任何清單、不能登入。
    /// </summary>
    Deleted = 2,
}
