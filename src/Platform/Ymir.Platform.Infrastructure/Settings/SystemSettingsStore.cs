using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ymir.Platform.Infrastructure.Persistence;
using Ymir.Platform.Settings;

namespace Ymir.Platform.Infrastructure.Settings;

/// <summary>
/// 系統設定（ADR-0010）。機密以 Data Protection 加密（purpose <see cref="Purpose"/>），
/// 金鑰沿用 cookie 已在使用的那一組（<c>Ymir:DataProtection:KeysPath</c>）；資料庫外洩時沒有金鑰無法解密。
/// </summary>
internal sealed partial class SystemSettingsStore(
    PlatformDbContext db,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<SystemSettingsStore> logger) : ISystemSettingsStore
{
    public const string Purpose = "Ymir.SystemSettings.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    public async Task<SystemSettingValue?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var setting = await FindAsync(key, tracking: false, cancellationToken).ConfigureAwait(false);
        return setting is null || setting.IsSecret ? null : new SystemSettingValue(setting.Value, setting.UpdatedAt, setting.UpdatedBy);
    }

    public Task SetAsync(string key, string value, string updatedBy, CancellationToken cancellationToken) =>
        UpsertAsync(key, value, isSecret: false, updatedBy, cancellationToken);

    public async Task<SystemSettingValue?> GetSecretAsync(string key, CancellationToken cancellationToken)
    {
        var setting = await FindAsync(key, tracking: false, cancellationToken).ConfigureAwait(false);
        if (setting is null || !setting.IsSecret)
        {
            return null;
        }

        try
        {
            return new SystemSettingValue(_protector.Unprotect(setting.Value), setting.UpdatedAt, setting.UpdatedBy);
        }
        catch (CryptographicException ex)
        {
            // 金鑰遺失或輪替後舊金鑰被刪除：只記錄 key 名稱，管理員需在網頁重新輸入。
            LogUnprotectFailed(logger, ex, key);
            return null;
        }
    }

    public Task SetSecretAsync(string key, string plaintext, string updatedBy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return UpsertAsync(key, _protector.Protect(plaintext), isSecret: true, updatedBy, cancellationToken);
    }

    public async Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var list = keys.ToList();
        await db.SystemSettings.Where(s => list.Contains(s.Key)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpsertAsync(string key, string value, bool isSecret, string updatedBy, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var setting = await FindAsync(key, tracking: true, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            db.SystemSettings.Add(SystemSetting.Create(key, value, isSecret, updatedBy, now));
        }
        else
        {
            setting.Update(value, isSecret, updatedBy, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<SystemSetting?> FindAsync(string key, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? db.SystemSettings : db.SystemSettings.AsNoTracking();
        return query.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to decrypt system setting {Key}; re-enter it in the admin console")]
    private static partial void LogUnprotectFailed(ILogger logger, Exception exception, string key);
}
