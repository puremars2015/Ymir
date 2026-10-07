using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ymir.VibeMaker.Application.PlatformMcp;

/// <summary>
/// 平台 MCP 服務目錄中的一項（ADR-0012 B.1）。只能由開發人員在版控的 <c>deploy/mcp/servers.json</c> 定義；
/// 管理介面只能改存取清單，不能新增服務或位址。
/// </summary>
/// <param name="Name">服務名稱：小寫英數字與 <c>-</c>，同時是 gateway 路徑 <c>/mcp/{name}</c> 與 Pi 的 MCP 名稱。</param>
/// <param name="Url">gateway 轉送的後端位址（只有 gateway 使用；不回傳給瀏覽器或 Agent）。</param>
/// <param name="CredentialEnv">後端需要的憑證所在的環境變數名稱（只存在 gateway 的部署 secret）；gateway 以 Bearer 送出。</param>
public sealed record McpServerDefinition(string Name, string Description, Uri Url, string? CredentialEnv);

/// <summary>服務目錄（<c>{"servers":[...]}</c>）。載入時驗證名稱、位址與環境變數名稱；不合法時拒絕啟動。</summary>
public sealed partial class McpCatalog
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, McpServerDefinition> _byName;

    public McpCatalog(IReadOnlyList<McpServerDefinition> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        Servers = servers;
        _byName = servers.ToDictionary(s => s.Name, StringComparer.Ordinal);
    }

    public static McpCatalog Empty { get; } = new([]);

    public IReadOnlyList<McpServerDefinition> Servers { get; }

    public McpServerDefinition? Find(string name) => _byName.GetValueOrDefault(name);

    public static bool IsValidName(string? name) => name is not null && ServerName().IsMatch(name);

    public static McpCatalog Load(string path) => Parse(File.ReadAllText(path), path);

    public static McpCatalog Parse(string json, string source = "servers.json")
    {
        McpCatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<McpCatalogDocument>(json, s_json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{source}: invalid JSON.", ex);
        }

        var servers = new List<McpServerDefinition>();
        foreach (var entry in document?.Servers ?? [])
        {
            if (!IsValidName(entry.Name))
            {
                throw new InvalidOperationException($"{source}: server name '{entry.Name}' must match {ServerName()}.");
            }

            if (servers.Any(s => s.Name == entry.Name))
            {
                throw new InvalidOperationException($"{source}: duplicate server name '{entry.Name}'.");
            }

            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"{source}: server '{entry.Name}' needs an absolute http(s) url.");
            }

            if (entry.CredentialEnv is { } env && !EnvironmentName().IsMatch(env))
            {
                throw new InvalidOperationException($"{source}: server '{entry.Name}' has an invalid credentialEnv.");
            }

            servers.Add(new McpServerDefinition(entry.Name!, entry.Description?.Trim() ?? string.Empty, url, entry.CredentialEnv));
        }

        return new McpCatalog(servers);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerName();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentName();

    private sealed record McpCatalogDocument(IReadOnlyList<McpCatalogEntry>? Servers);

    private sealed record McpCatalogEntry(string? Name, string? Description, string? Url, string? CredentialEnv);
}
