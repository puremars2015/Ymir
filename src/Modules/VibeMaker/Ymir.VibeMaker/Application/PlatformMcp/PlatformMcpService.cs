using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ymir.Platform.Auditing;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.PlatformMcp;

/// <summary><c>Ymir:Mcp</c>（ADR-0012 B）。沒有設定 <see cref="GatewayUrl"/> 時平台 MCP 停用。</summary>
public sealed class McpOptions
{
    public const string SectionName = "Ymir:Mcp";

    /// <summary>Agent container 連 gateway 用的位址（例如受限網路上的 <c>http://ymir-mcp-gateway:5310</c>）。</summary>
    public Uri? GatewayUrl { get; set; }

    /// <summary>服務目錄 <c>deploy/mcp/servers.json</c> 的路徑。</summary>
    public string? CatalogPath { get; set; }

    /// <summary>與 gateway 共用的簽章金鑰；只放部署 secret（不得進版控或 Agent container）。</summary>
    public string? TokenSigningKey { get; set; }

    public bool IsEnabled => GatewayUrl is not null;
}

/// <summary>某次執行可用的平台 MCP 服務與 token（token 只以環境變數傳入 Agent 程序）。</summary>
public sealed record PlatformMcpGrant(IReadOnlyList<PlatformMcpServer> Servers, string Token)
{
    /// <summary>record 預設的 ToString 會印出 token。</summary>
    public override string ToString() => $"PlatformMcpGrant {{ Servers = {string.Join(',', Servers.Select(s => s.Name))} }}";
}

/// <param name="Url">Agent 連線的 gateway 位址 <c>{GatewayUrl}/mcp/{name}</c>（不是後端位址）。</param>
public sealed record PlatformMcpServer(string Name, string Description, Uri Url);

public sealed record PlatformMcpServerState(string Name, string Description, bool Enabled, McpAccessMode Mode, IReadOnlyList<Guid> UserIds);

/// <summary>
/// 平台 MCP（ADR-0012 B）：目錄 ∩ 存取清單決定每位使用者可用的服務；執行前簽發每人專屬的短期 token。
/// </summary>
public sealed class PlatformMcpService(
    IVibeMakerDbContext db,
    McpCatalog catalog,
    IUserDirectory users,
    IOptions<McpOptions> options,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    /// <summary>token 最長有效期限（ADR-0012 B.3）。管理員撤銷存取最慢在這段時間內生效。</summary>
    public static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromHours(1);

    private readonly McpOptions _options = options.Value;

    public bool IsEnabled => _options.IsEnabled;

    public async Task<IReadOnlyList<PlatformMcpServerState>> ListAsync(CancellationToken cancellationToken)
    {
        var access = await db.McpServerAccess.AsNoTracking().ToDictionaryAsync(a => a.ServerName, cancellationToken).ConfigureAwait(false);
        return [.. catalog.Servers.Select(s => access.TryGetValue(s.Name, out var a)
            ? new PlatformMcpServerState(s.Name, s.Description, a.Enabled, a.Mode, a.UserIds)
            : new PlatformMcpServerState(s.Name, s.Description, false, McpAccessMode.Everyone, []))];
    }

    /// <returns>目錄中沒有這個服務時回傳 false。</returns>
    public async Task<bool> SetAccessAsync(string name, bool enabled, McpAccessMode mode, IReadOnlyCollection<Guid> userIds, string actor, CancellationToken cancellationToken)
    {
        if (catalog.Find(name) is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var access = await db.McpServerAccess.SingleOrDefaultAsync(a => a.ServerName == name, cancellationToken).ConfigureAwait(false);
        if (access is null)
        {
            access = McpServerAccess.Create(name, now);
            db.McpServerAccess.Add(access);
        }

        access.Update(enabled, mode, mode == McpAccessMode.SelectedUsers ? userIds : [], now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(actor, "admin.mcp.access.update", "mcp-server", name, AuditResult.Success, now, null), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>使用者可用的服務（目錄 ∩ 存取清單）；平台 MCP 停用或帳號不存在時為空。</summary>
    public async Task<IReadOnlyList<McpServerDefinition>> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!IsEnabled || catalog.Servers.Count == 0)
        {
            return [];
        }

        var user = await users.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatus.Active)
        {
            return [];
        }

        var access = await db.McpServerAccess.AsNoTracking().Where(a => a.Enabled).ToListAsync(cancellationToken).ConfigureAwait(false);
        var isAdmin = user.Role == UserRole.Admin;
        return [.. catalog.Servers.Where(s => access.Any(a => a.ServerName == s.Name && a.Allows(userId, isAdmin)))];
    }

    /// <summary>使用者在外掛市集停用的平台服務（ADR-0030 §3；沒有紀錄 = 啟用）。</summary>
    public async Task<IReadOnlySet<string>> DisabledByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var keys = await db.UserPluginSettings.AsNoTracking()
            .Where(s => s.UserId == userId && !s.Enabled && s.PluginKey.StartsWith(PluginKeyPrefix))
            .Select(s => s.PluginKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return keys.Select(k => k[PluginKeyPrefix.Length..]).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>外掛市集中平台 MCP 服務的 key 前綴（ADR-0030 §3）。</summary>
    public const string PluginKeyPrefix = "mcp:";

    /// <summary>給 Agent 看的 OneDrive 內建服務說明（ADR-0030 §4）；Agent 依它判斷何時使用。</summary>
    public const string OneDriveDescription =
        "使用者自己的 OneDrive：search（以名稱搜尋）、list_folder（列出資料夾）、download（下載檔案到工作目錄 onedrive/）、"
        + "upload（把工作目錄的檔案上傳到 OneDrive）、create_folder、move（移動或改名）、delete（刪除到 OneDrive 回收筒）。"
        + "只在使用者的要求需要 OneDrive 時使用；刪除、覆蓋前先確認使用者確實要求。";

    /// <summary>
    /// 執行前準備：有可用服務時簽發 token（期限 = 執行逾時 + 5 分鐘，最長 1 小時）；沒有時回傳 null，Agent 不會看到任何平台服務。
    /// 使用者在外掛市集停用的服務不帶入（ADR-0030 §3）。<paramref name="includeOneDrive"/> 為 true 時加入內建的 OneDrive 服務
    /// （由呼叫端確認能力、連線與使用者開關）；token 帶 execution id，API 只在這次執行中接受它（ADR-0030 §2）。
    /// </summary>
    public async Task<PlatformMcpGrant?> PrepareRunAsync(Guid userId, Guid executionId, TimeSpan executionTimeout, bool includeOneDrive, CancellationToken cancellationToken)
    {
        var disabled = await DisabledByUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var gateway = _options.GatewayUrl?.ToString().TrimEnd('/');
        List<PlatformMcpServer> servers = [.. (await ResolveAsync(userId, cancellationToken).ConfigureAwait(false))
            .Where(s => !disabled.Contains(s.Name))
            .Select(s => new PlatformMcpServer(s.Name, s.Description, new Uri($"{gateway}/mcp/{s.Name}")))];
        if (includeOneDrive && IsEnabled)
        {
            servers.Add(new PlatformMcpServer(McpCatalog.OneDriveServerName, OneDriveDescription, new Uri($"{gateway}/mcp/{McpCatalog.OneDriveServerName}")));
        }

        if (servers.Count == 0)
        {
            return null;
        }

        var lifetime = executionTimeout + TimeSpan.FromMinutes(5);
        var expiresAt = timeProvider.GetUtcNow() + (lifetime > MaxTokenLifetime ? MaxTokenLifetime : lifetime);
        var token = McpGatewayToken.Issue(_options.TokenSigningKey!, new McpGatewayClaims(userId, [.. servers.Select(s => s.Name)], expiresAt, executionId));
        return new PlatformMcpGrant(servers, token);
    }
}
