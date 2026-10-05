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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(MasterKey);
}
