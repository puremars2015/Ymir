namespace Ymir.VibeMaker.Application.Models;

/// <summary>
/// 模型入口（MVP 為 LiteLLM，SA §19）。為使用者的 runtime 發放短效、受限的 virtual key，
/// container 內永遠不放 master key（ADR-0004）。
/// </summary>
public interface IModelGateway
{
    /// <exception cref="ModelCredentialException">模型入口無法發放金鑰（例如 LiteLLM 無法連線）。</exception>
    Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken);

    Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken);

    /// <summary>模型入口是否提供用量與預算（LiteLLM 有；開發用的 gateway 沒有）。</summary>
    bool SupportsUsage { get; }

    /// <summary>
    /// 套用使用者的每月模型預算（null 表示不限制），由模型入口強制（ADR-0004、ADR-0011）。
    /// 發 key 時也會套用，所以這裡失敗只影響「改預算後到下次發 key 之前」的這段時間。
    /// </summary>
    /// <exception cref="ModelCredentialException">模型入口無法連線或拒絕。</exception>
    Task ApplyUserBudgetAsync(Guid userId, decimal? monthlyBudget, CancellationToken cancellationToken);

    /// <summary>使用者本期的花費與預算；沒有用量資料（使用者還沒用過）時回傳 null。</summary>
    /// <exception cref="ModelCredentialException">模型入口無法連線或拒絕。</exception>
    Task<ModelBudgetStatus?> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// 各使用者在 <paramref name="fromDate"/> ～ <paramref name="toDate"/>（含，UTC 日期）的模型用量。
    /// 單一使用者查詢失敗時該使用者不在結果中，不讓整批失敗。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ModelUserUsage>> GetUsageAsync(IReadOnlyCollection<Guid> userIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}

/// <param name="MonthlyBudget">使用者每月預算（美元）；null 表示不限制。</param>
/// <param name="MaxBudget">每把 key 的預算（舊設定，相容用）。</param>
public sealed record RuntimeCredentialRequest(
    Guid UserId,
    Guid RuntimeId,
    IReadOnlyList<string> AllowedModels,
    TimeSpan Lifetime,
    decimal? MaxBudget,
    decimal? MonthlyBudget = null);

/// <param name="Spend">本期（預算週期內）已花費的金額（美元）。</param>
/// <param name="MaxBudget">預算上限；null 表示不限制。</param>
/// <param name="ResetAt">預算下次重置的時間。</param>
public sealed record ModelBudgetStatus(decimal Spend, decimal? MaxBudget, DateTimeOffset? ResetAt)
{
    public bool IsExceeded => MaxBudget is { } max && Spend >= max;
}

/// <param name="Spend">期間內的模型費用（美元，依 LiteLLM 設定的單價計算；沒有單價時為 0）。</param>
/// <param name="Budget">本期預算狀態（可能取不到）。</param>
public sealed record ModelUserUsage(decimal Spend, long PromptTokens, long CompletionTokens, long Requests, ModelBudgetStatus? Budget);

/// <param name="KeyId">撤銷用的識別（LiteLLM 的 hashed token），本身不能拿來呼叫模型。</param>
/// <param name="ApiKey">給 Agent 使用的 virtual key；只存在記憶體，不寫資料庫、不寫 log。</param>
public sealed record RuntimeModelCredential(string KeyId, string ApiKey, DateTimeOffset ExpiresAt)
{
    /// <summary>record 預設的 ToString 會印出所有屬性；金鑰不得進 log（SA §12）。</summary>
    public override string ToString() => $"RuntimeModelCredential {{ KeyId = {KeyId}, ExpiresAt = {ExpiresAt:O} }}";
}

/// <summary>模型入口無法發放或撤銷金鑰。訊息只給 server log，不回傳瀏覽器。</summary>
public sealed class ModelCredentialException(string message, Exception? innerException = null) : Exception(message, innerException);
