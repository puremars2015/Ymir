using System.Text.Json;
using Ymir.Maintenance;
using Ymir.Platform.Settings;

namespace Ymir.Api.Maintenance;

/// <summary>保存的回收政策與更新資訊；沒有保存過時 <see cref="Stored"/> 為 null（使用預設：自動回收關閉）。</summary>
public sealed record StoredReclamationPolicy(ReclamationPolicy Policy, SystemSettingValue? Stored);

/// <summary>
/// 資源回收政策（ADR-0025 §1、§3）存在 <c>platform.system_settings</c>（ADR-0010 的模式）；不含機密，以明文 JSON 保存。
/// API 是政策的來源，<see cref="MaintenanceSyncWorker"/> 定期同步到主機維護服務（服務自己也保存最後一次同步的政策，API 暫時離線仍照常運作）。
/// </summary>
internal static class ReclamationPolicyStore
{
    public const string Key = "maintenance.reclamation_policy";

    /// <summary>上次匯入稽核的回收紀錄 id。</summary>
    public const string LastImportedRunKey = "maintenance.last_imported_run";

    public static async Task<StoredReclamationPolicy> GetAsync(ISystemSettingsStore store, CancellationToken cancellationToken)
    {
        var stored = await store.GetAsync(Key, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return new StoredReclamationPolicy(ReclamationPolicy.Default, null);
        }

        try
        {
            var policy = JsonSerializer.Deserialize<ReclamationPolicy>(stored.Value, MaintenanceProtocol.JsonOptions);
            // 損毀或不合法的值：視為自動回收關閉（保守），不會因此刪除任何東西。
            return policy is not null && policy.Validate() is null ? new StoredReclamationPolicy(policy, stored) : new StoredReclamationPolicy(ReclamationPolicy.Default, stored);
        }
        catch (JsonException)
        {
            return new StoredReclamationPolicy(ReclamationPolicy.Default, stored);
        }
    }

    public static Task SaveAsync(ISystemSettingsStore store, ReclamationPolicy policy, string updatedBy, CancellationToken cancellationToken) =>
        store.SetAsync(Key, JsonSerializer.Serialize(policy, MaintenanceProtocol.JsonOptions), updatedBy, cancellationToken);
}
