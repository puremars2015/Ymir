namespace Ymir.Platform.Settings;

/// <summary>platform.system_settings 的一筆設定（ADR-0010）。<see cref="IsSecret"/> 為 true 時 <see cref="Value"/> 是密文。</summary>
public sealed class SystemSetting
{
    public const int KeyMaxLength = 100;
    public const int ValueMaxLength = 8000;

    private SystemSetting()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public bool IsSecret { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string UpdatedBy { get; private set; } = string.Empty;

    public static SystemSetting Create(string key, string value, bool isSecret, string updatedBy, DateTimeOffset now)
    {
        var setting = new SystemSetting { Key = Validate(key) };
        setting.Update(value, isSecret, updatedBy, now);
        return setting;
    }

    public void Update(string value, bool isSecret, string updatedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);
        if (value.Length > ValueMaxLength)
        {
            throw new ArgumentException($"Setting value must be at most {ValueMaxLength} characters.", nameof(value));
        }

        Value = value;
        IsSecret = isSecret;
        UpdatedBy = updatedBy.Length <= 200 ? updatedBy : updatedBy[..200];
        UpdatedAt = now;
    }

    private static string Validate(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Length <= KeyMaxLength ? key : throw new ArgumentException($"Setting key must be at most {KeyMaxLength} characters.", nameof(key));
    }
}
