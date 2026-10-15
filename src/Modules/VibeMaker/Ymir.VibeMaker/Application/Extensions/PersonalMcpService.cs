using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Ymir.Platform.Auditing;
using Ymir.VibeMaker.Application.Persistence;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Extensions;

public interface IPersonalMcpSecrets
{
    string Protect(Guid userId, string token);
    string Unprotect(Guid userId, string ciphertext);
}

public interface IPersonalMcpProbe
{
    Task<bool> TestAsync(Uri url, string? token, CancellationToken cancellationToken);
}

public sealed record PersonalMcpInfo(Guid Id, string Name, string Url, bool Enabled, bool HasCredential, DateTimeOffset CreatedAt);
public sealed record PersonalMcpList(bool Allowed, IReadOnlyList<PersonalMcpInfo> Servers);
public sealed record PersonalMcpTestResult(bool Success, string Message);

public sealed record PersonalMcpGrant(Guid Id, string Url, string? Token)
{
    public string ServerName => "personal-" + Id.ToString("N");
    public string TokenVariable => "YMIR_PERSONAL_MCP_" + Id.ToString("N").ToUpperInvariant();
    public override string ToString() => $"PersonalMcpGrant {{ Id = {Id} }}";
}

public sealed class PersonalMcpException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>專用註冊鎖；不在整次 Agent 執行期間持有。</summary>
public sealed class PersonalMcpLocks
{
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public SemaphoreSlim For(Guid userId) => _locks[(uint)userId.GetHashCode() % (uint)_locks.Length];
}

public sealed partial class PersonalMcpService(
    IVibeMakerDbContext db, IExtensionPolicy policy, IPersonalMcpSecrets secrets,
    IPersonalMcpProbe probe, PersonalMcpLocks locks, IAuditLog audit, TimeProvider clock)
{
    public async Task<PersonalMcpList> ListAsync(Guid userId, CancellationToken ct)
    {
        var allowed = await AllowedAsync(userId, ct).ConfigureAwait(false);
        var items = await db.PersonalMcpServers.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
        return new(allowed, items.Select(Info).ToList());
    }

    public async Task<PersonalMcpInfo> AddAsync(Guid userId, string actor, string name, string url, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) throw new PersonalMcpException("請填寫 MCP 名稱與 HTTPS 網址。");
        name = name.Trim();
        url = url.Trim();
        if (!NamePattern().IsMatch(name) || !IsValidUrl(url) || token is { Length: > 4096 } || !string.IsNullOrEmpty(token) && !TokenPattern().IsMatch(token))
            throw new PersonalMcpException("請使用小寫英數與連字號名稱、公開 HTTPS 網址，以及有效的 Bearer token。");
        var gate = locks.For(userId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RequireAllowedAsync(userId, ct).ConfigureAwait(false);
            if (await db.PersonalMcpServers.CountAsync(s => s.UserId == userId, ct).ConfigureAwait(false) >= 20)
                throw new PersonalMcpException("個人 MCP 數量已達 20 個上限。", 409);
            if (await db.PersonalMcpServers.AnyAsync(s => s.UserId == userId && s.Name == name, ct).ConfigureAwait(false))
                throw new PersonalMcpException("此 MCP 名稱已存在。", 409);
            var item = PersonalMcpServer.Create(userId, name, new Uri(url).AbsoluteUri, string.IsNullOrEmpty(token) ? null : secrets.Protect(userId, token), clock.GetUtcNow());
            db.PersonalMcpServers.Add(item);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await AuditAsync(actor, "personal_mcp.add", item.Id, ct).ConfigureAwait(false);
            return Info(item);
        }
        finally { gate.Release(); }
    }

    public async Task<PersonalMcpInfo> SetEnabledAsync(Guid userId, string actor, Guid id, bool enabled, CancellationToken ct)
    {
        var gate = locks.For(userId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var item = await FindAsync(userId, id, ct).ConfigureAwait(false);
            if (enabled) await RequireAllowedAsync(userId, ct).ConfigureAwait(false);
            item.SetEnabled(enabled);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await AuditAsync(actor, enabled ? "personal_mcp.enable" : "personal_mcp.disable", id, ct).ConfigureAwait(false);
            return Info(item);
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(Guid userId, string actor, Guid id, CancellationToken ct)
    {
        var gate = locks.For(userId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var item = await FindAsync(userId, id, ct).ConfigureAwait(false);
            db.PersonalMcpServers.Remove(item);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await AuditAsync(actor, "personal_mcp.remove", id, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<PersonalMcpTestResult> TestAsync(Guid userId, string actor, Guid id, CancellationToken ct)
    {
        await RequireAllowedAsync(userId, ct).ConfigureAwait(false);
        var item = await FindAsync(userId, id, ct).ConfigureAwait(false);
        var success = await probe.TestAsync(new Uri(item.Url), item.ProtectedToken is { } ciphertext ? secrets.Unprotect(userId, ciphertext) : null, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEntry(actor, "personal_mcp.test", "personal_mcp", id.ToString("D"), success ? AuditResult.Success : AuditResult.Failure, clock.GetUtcNow(), null), ct).ConfigureAwait(false);
        return new(success, success ? "MCP initialize 連線測試成功，未呼叫任何工具。" : "連線測試失敗；請檢查服務、憑證與公開網路設定。");
    }

    public async Task<IReadOnlyList<PersonalMcpGrant>> PrepareRunAsync(Guid userId, CancellationToken ct)
    {
        if (!await AllowedAsync(userId, ct).ConfigureAwait(false)) return [];
        var items = await db.PersonalMcpServers.AsNoTracking().Where(s => s.UserId == userId && s.Enabled).OrderBy(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        return items.Select(s => new PersonalMcpGrant(s.Id, s.Url, s.ProtectedToken is { } ciphertext ? secrets.Unprotect(userId, ciphertext) : null)).ToList();
    }

    public static bool IsValidUrl(string url) => url.Length <= 2048 && !url.Any(char.IsControl)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Port == 443
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.HostNameType == UriHostNameType.Dns
        && uri.Host.Contains('.', StringComparison.Ordinal) && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> AllowedAsync(Guid userId, CancellationToken ct)
    {
        var effective = await policy.ResolveAsync(userId, ct).ConfigureAwait(false);
        return effective.Mcp && effective.Internet;
    }

    private async Task RequireAllowedAsync(Guid userId, CancellationToken ct)
    {
        if (!await AllowedAsync(userId, ct).ConfigureAwait(false)) throw new PersonalMcpException("管理員尚未開放 MCP 或對外連線能力。", 403);
    }

    private async Task<PersonalMcpServer> FindAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.PersonalMcpServers.SingleOrDefaultAsync(s => s.UserId == userId && s.Id == id, ct).ConfigureAwait(false)
        ?? throw new PersonalMcpException("找不到個人 MCP。", 404);

    private static PersonalMcpInfo Info(PersonalMcpServer item) => new(item.Id, item.Name, item.Url, item.Enabled, item.ProtectedToken is not null, item.CreatedAt);
    private Task AuditAsync(string actor, string action, Guid id, CancellationToken ct) => audit.WriteAsync(new AuditEntry(actor, action, "personal_mcp", id.ToString("D"), AuditResult.Success, clock.GetUtcNow(), null), ct);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z0-9._~+/-]+=*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
