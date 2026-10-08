namespace Ymir.VibeMaker.Contracts.Extensions;

/// <summary>目前使用者的擴充能力與自建擴充（ADR-0012 A.6）；只回自己的資料。</summary>
/// <param name="SkillsAllowed">管理員是否允許自建 skill（全域預設或個人覆寫的結果）。</param>
/// <param name="McpAllowed">管理員是否允許自建 MCP server。</param>
/// <param name="InternetAllowed">Agent 能否對外連線（ADR-0012 A.8）；不允許時只連得到平台的模型與服務。</param>
/// <param name="OneDriveAllowed">能否連結自己的 OneDrive（ADR-0013）。</param>
/// <param name="InventoryAvailable">是否讀到了自建擴充清單；還沒有執行環境時為 false（第一次送訊息後才會建立）。</param>
/// <param name="Skills">自建 skill 的名稱（目錄名稱）。</param>
/// <param name="McpServers">自建 MCP server 的名稱。</param>
/// <param name="PlatformMcpServers">管理員開放給我的平台 MCP 服務（ADR-0012 B.5），只有名稱與說明。</param>
public sealed record MyExtensionsResponse(
    bool SkillsAllowed,
    bool McpAllowed,
    bool InternetAllowed,
    bool OneDriveAllowed,
    bool InventoryAvailable,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> McpServers,
    IReadOnlyList<PlatformMcpServerSummary> PlatformMcpServers);

/// <summary>平台 MCP 服務的摘要；不含後端位址或任何憑證。</summary>
public sealed record PlatformMcpServerSummary(string Name, string Description);
