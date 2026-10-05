namespace Ymir.Edge;

/// <summary>
/// 對外入口（Cloudflare Tunnel，ADR-0006）的設定，區段 <c>Ymir:PublicEdge</c>。
/// 關閉時（預設）API 的行為與內網部署完全相同。
/// </summary>
public sealed class PublicEdgeOptions
{
    public const string SectionName = "Ymir:PublicEdge";

    /// <summary>是否經由 Cloudflare Tunnel 對外公開。</summary>
    public bool Enabled { get; set; }

    /// <summary>對外的主機名稱（例如 <c>ymir.example.com</c>），不含 scheme 與 port；也是唯一允許的外部 Host header。</summary>
    public string? PublicHostname { get; set; }

    /// <summary>
    /// 可信任的 proxy（cloudflared）IP。只有來自這些位址的 <c>X-Forwarded-*</c> 會被採用；
    /// 未設定時只信任 loopback（cloudflared 與 API 在同一台主機）。
    /// </summary>
    public IList<string> KnownProxies { get; } = [];
}
