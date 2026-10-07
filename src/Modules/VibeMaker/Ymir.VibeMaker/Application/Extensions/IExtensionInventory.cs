namespace Ymir.VibeMaker.Application.Extensions;

/// <summary>使用者自建的擴充（名稱清單，不含內容）。</summary>
public sealed record ExtensionInventory(IReadOnlyList<string> Skills, IReadOnlyList<string> McpServers);

/// <summary>
/// 掃描使用者 runtime 內的自建擴充（ADR-0012 A.6）。實作在 Infrastructure，經 runtime 內的程序讀取，
/// 不接受任何路徑參數（位置固定為 Pi 的 agent dir）。
/// </summary>
public interface IExtensionInventory
{
    /// <summary>還沒有 runtime 時回傳 null（不為了查詢而建立 container）。</summary>
    Task<ExtensionInventory?> ListAsync(Guid userId, CancellationToken cancellationToken);
}
