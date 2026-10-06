namespace Ymir.Platform.Settings;

/// <summary>
/// 可由管理介面修改的系統設定（ADR-0010），存在 <c>platform.system_settings</c>。
/// 機密值（<see cref="SetSecretAsync"/>）以 Data Protection 加密後才寫入資料庫，讀取時解密；
/// 一般值（<see cref="SetAsync"/>）以明文保存，不得用來存放機密。
/// </summary>
public interface ISystemSettingsStore
{
    Task<SystemSettingValue?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, string updatedBy, CancellationToken cancellationToken);

    /// <summary>解密後的機密值；金鑰遺失或資料損毀無法解密時回傳 null（不拋出，讓呼叫端退回其他設定來源）。</summary>
    Task<SystemSettingValue?> GetSecretAsync(string key, CancellationToken cancellationToken);

    Task SetSecretAsync(string key, string plaintext, string updatedBy, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

public sealed record SystemSettingValue(string Value, DateTimeOffset UpdatedAt, string UpdatedBy);
