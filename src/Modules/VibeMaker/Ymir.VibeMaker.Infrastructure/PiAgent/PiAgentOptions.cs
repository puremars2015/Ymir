namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>設定區段 <c>VibeMaker:Pi</c>。</summary>
public sealed class PiAgentOptions
{
    public const string SectionName = "VibeMaker:Pi";

    /// <summary>Runtime 內的 Pi 執行檔。</summary>
    public string Executable { get; set; } = "pi";

    /// <summary>OpenAI 相容的模型端點（LiteLLM），必須是 runtime 內可連到的位址。</summary>
    public Uri ModelBaseUrl { get; set; } = new("http://127.0.0.1:4000/v1");

    /// <summary>Pi models.json 中的 provider 名稱。</summary>
    public string ProviderName { get; set; } = "ymir";

    public string ModelId { get; set; } = "default";

    /// <summary>
    /// 開發用的固定 API key。正式環境改由 <c>IModelGateway</c> 為每個 runtime 發放 virtual key（ADR-0004）。
    /// </summary>
    public string? DevelopmentApiKey { get; set; }

    /// <summary>送出 abort 後等待 Pi 收尾的時間，超過即強制結束程序。</summary>
    public TimeSpan AbortGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>是否啟用 Pi 的自動重試（模型端暫時錯誤時）。</summary>
    public bool AutoRetry { get; set; } = true;
}
