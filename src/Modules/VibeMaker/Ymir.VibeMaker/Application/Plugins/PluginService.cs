using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Connectors.OneDrive;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Application.PlatformMcp;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Plugins;

public enum PluginKind
{
    /// <summary>平台內建的 OneDrive（ADR-0030）。</summary>
    OneDrive,

    /// <summary>管理員開放的平台 MCP 服務（ADR-0012 B）。</summary>
    PlatformMcp,
}

/// <summary>外掛市集的一張卡片（ADR-0030 §1）。不含任何憑證或後端位址。</summary>
/// <param name="Allowed">管理員是否允許這位使用者使用。</param>
/// <param name="Installed">是否已安裝（OneDrive：已連結帳號；平台 MCP：管理員開放即視為已安裝）。</param>
/// <param name="Enabled">使用者自己的開關。</param>
/// <param name="AgentReady">下一次執行時 Agent 是否看得到這個外掛（以上條件都成立，且平台設定完整）。</param>
/// <param name="Notice">給使用者看的限制說明；沒有時為 null。</param>
public sealed record PluginInfo(
    string Key,
    PluginKind Kind,
    string Name,
    string Description,
    bool Allowed,
    bool Installed,
    bool Enabled,
    bool AgentReady,
    string? Notice);

public enum PluginUpdateResult
{
    Updated,

    /// <summary>不是這位使用者市集裡的外掛。</summary>
    NotFound,

    /// <summary>管理員沒有開放，不能啟用（停用一律允許）。</summary>
    NotAllowed,
}

/// <summary>
/// 外掛市集（ADR-0030）：管理員決定「誰能用」（擴充政策、平台 MCP 存取清單），使用者決定「要不要用」（<see cref="UserPluginSetting"/>）。
/// </summary>
public sealed class PluginService(
    IVibeMakerDbContext db,
    OneDriveConnectionService oneDrive,
    PlatformMcpService platformMcp,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const string OneDriveKey = "onedrive";

    public const string OneDriveDescription =
        "讓 Agent 在對話中自己搜尋、讀取、上傳、移動與刪除你 OneDrive 上的檔案。";

    public async Task<IReadOnlyList<PluginInfo>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(userId, cancellationToken).ConfigureAwait(false);
        List<PluginInfo> plugins = [await OneDriveAsync(userId, settings, cancellationToken).ConfigureAwait(false)];
        foreach (var server in await platformMcp.ResolveAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            var key = PlatformMcpService.PluginKeyPrefix + server.Name;
            var enabled = settings.GetValueOrDefault(key, true);
            plugins.Add(new PluginInfo(key, PluginKind.PlatformMcp, server.Name, server.Description, true, true, enabled, enabled, null));
        }

        return plugins;
    }

    /// <returns>更新結果與更新後的卡片（<see cref="PluginUpdateResult.Updated"/> 時才有）。</returns>
    public async Task<(PluginUpdateResult Result, PluginInfo? Plugin)> SetEnabledAsync(Guid userId, string key, bool enabled, string actor, CancellationToken cancellationToken)
    {
        var current = (await ListAsync(userId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(p => p.Key == key);
        if (current is null)
        {
            return (PluginUpdateResult.NotFound, null);
        }

        if (enabled && !current.Allowed)
        {
            return (PluginUpdateResult.NotAllowed, null);
        }

        var now = timeProvider.GetUtcNow();
        var setting = await db.UserPluginSettings.SingleOrDefaultAsync(s => s.UserId == userId && s.PluginKey == key, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            db.UserPluginSettings.Add(UserPluginSetting.Create(userId, key, enabled, now));
        }
        else
        {
            setting.Set(enabled, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(
            new AuditEntry(actor, enabled ? "plugin.enable" : "plugin.disable", "plugin", key, AuditResult.Success, now, null),
            cancellationToken).ConfigureAwait(false);
        var updated = (await ListAsync(userId, cancellationToken).ConfigureAwait(false)).First(p => p.Key == key);
        return (PluginUpdateResult.Updated, updated);
    }

    /// <summary>
    /// Agent 這次能否使用 OneDrive 工具（ADR-0030 §2）：管理員允許、已連結且授權有效、使用者啟用、平台 MCP 已設定。
    /// 執行前與每次工具呼叫都會檢查，撤銷能力或停用外掛後立即生效。
    /// </summary>
    public async Task<bool> IsOneDriveAgentEnabledAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!platformMcp.IsEnabled)
        {
            return false;
        }

        var enabled = await db.UserPluginSettings.AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.PluginKey == OneDriveKey && s.Enabled, cancellationToken).ConfigureAwait(false);
        if (!enabled)
        {
            return false;
        }

        var status = await oneDrive.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
        return status.Allowed && status.Available && status.State == OneDriveLinkState.Connected;
    }

    private async Task<PluginInfo> OneDriveAsync(Guid userId, Dictionary<string, bool> settings, CancellationToken cancellationToken)
    {
        var status = await oneDrive.GetStatusAsync(userId, cancellationToken).ConfigureAwait(false);
        var enabled = settings.GetValueOrDefault(OneDriveKey, false);
        var installed = status.State == OneDriveLinkState.Connected;
        var notice = !status.Available ? "企業帳號登入尚未設定，暫時無法連結 OneDrive。"
            : !status.Allowed ? "管理員尚未開放 OneDrive，請洽管理員。"
            : status.State == OneDriveLinkState.NeedsReauth ? "OneDrive 授權已失效，請重新連結。"
            : !platformMcp.IsEnabled ? "管理員尚未設定 MCP gateway，Agent 暫時無法使用 OneDrive；同步資料夾不受影響。"
            : null;
        return new PluginInfo(
            OneDriveKey,
            PluginKind.OneDrive,
            "OneDrive",
            OneDriveDescription,
            status.Allowed && status.Available,
            installed,
            enabled,
            enabled && installed && status.Allowed && status.Available && platformMcp.IsEnabled,
            notice);
    }

    private async Task<Dictionary<string, bool>> SettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserPluginSettings.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToDictionaryAsync(s => s.PluginKey, s => s.Enabled, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
}
