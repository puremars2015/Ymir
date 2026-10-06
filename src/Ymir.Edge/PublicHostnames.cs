namespace Ymir.Edge;

/// <summary>
/// 對外網域的覆寫來源（ADR-0010）：管理介面設定的網域優先於部署設定 <c>Ymir:PublicEdge:PublicHostname</c>。
/// 實作必須快取（每個請求都會呼叫）。
/// </summary>
public interface IPublicHostnameSource
{
    /// <summary>管理介面設定的網域；沒有設定時回傳 null（使用部署設定）。</summary>
    ValueTask<string?> GetOverrideAsync(CancellationToken cancellationToken);
}

/// <summary>預設：只用部署設定。</summary>
internal sealed class DeploymentPublicHostname : IPublicHostnameSource
{
    public ValueTask<string?> GetOverrideAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
}

public static class PublicEdgeHostnames
{
    /// <summary>DNS 主機名稱，不含 scheme、port、路徑（例如 <c>ymir.example.com</c>）。</summary>
    public static bool IsValid(string? hostname) =>
        !string.IsNullOrWhiteSpace(hostname)
        && hostname.Length <= 253
        && hostname == hostname.Trim()
        && hostname.Contains('.', StringComparison.Ordinal)
        && Uri.CheckHostName(hostname) == UriHostNameType.Dns;
}
