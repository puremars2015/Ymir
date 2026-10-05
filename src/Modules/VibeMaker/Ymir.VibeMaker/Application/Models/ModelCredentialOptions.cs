namespace Ymir.VibeMaker.Application.Models;

/// <summary>
/// 發給 runtime 的 virtual key 的限制（設定區段 <c>VibeMaker:LiteLlm</c>，ADR-0004）。
/// 連線資訊（BaseUrl、MasterKey）在 Infrastructure 的 LiteLlmOptions，同一個區段。
/// </summary>
public sealed class ModelCredentialOptions
{
    public const string SectionName = "VibeMaker:LiteLlm";

    /// <summary>key 可以使用的模型（LiteLLM 的 model_name）；未設定時由 Infrastructure 補上 Pi 使用的模型，不會發出不限模型的 key。</summary>
    public IList<string> AllowedModels { get; } = [];

    /// <summary>key 有效期；到期前 <see cref="RenewBefore"/> 會換發新的。</summary>
    public TimeSpan KeyLifetime { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan RenewBefore { get; set; } = TimeSpan.FromHours(1);

    /// <summary>每把 key 的預算上限（美元）；null 表示不限制。</summary>
    public decimal? MaxBudget { get; set; }
}
