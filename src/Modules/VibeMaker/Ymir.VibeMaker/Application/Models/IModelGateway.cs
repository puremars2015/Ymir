namespace Ymir.VibeMaker.Application.Models;

/// <summary>
/// 模型入口（MVP 為 LiteLLM，SA §19）。Runtime 啟動時發放短效、受限的 virtual key，
/// container 內永遠不放 master key。見 ADR-0004（Sprint 4 實作）。
/// </summary>
public interface IModelGateway
{
    Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken);

    Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken);
}

public sealed record RuntimeCredentialRequest(
    Guid UserId,
    Guid RuntimeId,
    IReadOnlyList<string> AllowedModels,
    TimeSpan Lifetime,
    decimal? MaxBudget);

/// <param name="BaseUrl">Container 內可連到的 OpenAI 相容端點。</param>
public sealed record RuntimeModelCredential(string KeyId, string ApiKey, Uri BaseUrl, DateTimeOffset ExpiresAt);
