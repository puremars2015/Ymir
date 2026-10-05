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
}

public sealed record RuntimeCredentialRequest(
    Guid UserId,
    Guid RuntimeId,
    IReadOnlyList<string> AllowedModels,
    TimeSpan Lifetime,
    decimal? MaxBudget);

/// <param name="KeyId">撤銷用的識別（LiteLLM 的 hashed token），本身不能拿來呼叫模型。</param>
/// <param name="ApiKey">給 Agent 使用的 virtual key；只存在記憶體，不寫資料庫、不寫 log。</param>
public sealed record RuntimeModelCredential(string KeyId, string ApiKey, DateTimeOffset ExpiresAt)
{
    /// <summary>record 預設的 ToString 會印出所有屬性；金鑰不得進 log（SA §12）。</summary>
    public override string ToString() => $"RuntimeModelCredential {{ KeyId = {KeyId}, ExpiresAt = {ExpiresAt:O} }}";
}

/// <summary>模型入口無法發放或撤銷金鑰。訊息只給 server log，不回傳瀏覽器。</summary>
public sealed class ModelCredentialException(string message, Exception? innerException = null) : Exception(message, innerException);
