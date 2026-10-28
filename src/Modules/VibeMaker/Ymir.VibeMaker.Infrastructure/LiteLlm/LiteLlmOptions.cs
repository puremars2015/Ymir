namespace Ymir.VibeMaker.Infrastructure.LiteLlm;

/// <summary>
/// API 端連線 LiteLLM 的設定（區段 <c>VibeMaker:LiteLlm</c>，ADR-0004）。
/// key 的限制（模型、有效期、預算）在同一區段，由 <c>ModelCredentialOptions</c> 讀取。
/// </summary>
public sealed class LiteLlmOptions
{
    public const string SectionName = "VibeMaker:LiteLlm";

    /// <summary>API 連 LiteLLM proxy 的位址（不含 <c>/v1</c>），例如 <c>http://127.0.0.1:4000</c>。</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// LiteLLM master key：只用來發放 / 撤銷 virtual key，只存在 API 的 secret 設定，
    /// 絕不放進 Agent container（ADR-0004、CLAUDE.md 安全紅線）。
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// Cloudflare Access service token 的用戶端識別碼（<c>CF-Access-Client-Id</c>）：LiteLLM 的管理端點放在 Access 後面時使用
    /// （ADR-0004 補充）。只放在 API 的 secret 設定，只附加在送往 <see cref="BaseUrl"/> 同一來源的請求。
    /// </summary>
    public string? CfAccessClientId { get; set; }

    /// <summary>Cloudflare Access service token 的用戶端密碼（<c>CF-Access-Client-Secret</c>）；規則同 <see cref="CfAccessClientId"/>，不得進 Agent container。</summary>
    public string? CfAccessClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(MasterKey);

    public bool HasCloudflareAccess => !string.IsNullOrWhiteSpace(CfAccessClientId) && !string.IsNullOrWhiteSpace(CfAccessClientSecret);
}
