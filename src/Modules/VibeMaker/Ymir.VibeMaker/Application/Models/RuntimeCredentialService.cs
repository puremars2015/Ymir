using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ymir.VibeMaker.Application.Models;

public enum RuntimeCredentialPurpose { Agent, KnowledgeEmbedding, KnowledgeAnswer }

/// <summary>
/// 每位使用者依 Agent、知識庫索引與回答用途分別快取 virtual key，並在到期前換發（ADR-0004）。
/// key 不寫資料庫：API 重啟後重新發一把，舊 key 最晚在有效期後自動失效。
/// </summary>
public sealed class RuntimeCredentialService(
    IModelGateway gateway,
    IOptions<ModelCredentialOptions> options,
    TimeProvider timeProvider,
    ILogger<RuntimeCredentialService> logger)
{
    private sealed record CachedCredential(RuntimeModelCredential Credential, IReadOnlyList<string> Models);
    // 合併知識庫與 Agent 模型權限後，分開用途，避免背景索引換 key 撤銷仍在執行的 Agent。
    private readonly ConcurrentDictionary<(Guid UserId, RuntimeCredentialPurpose Purpose), CachedCredential> _credentials = new();
    private readonly ConcurrentDictionary<(Guid UserId, RuntimeCredentialPurpose Purpose), SemaphoreSlim> _locks = new();

    /// <exception cref="ModelCredentialException">無法取得 key。</exception>
    /// <param name="monthlyBudget">目前執行政策的每月預算（ADR-0011）；發新 key 時一併套用到模型入口的使用者。</param>
    public async Task<RuntimeModelCredential> GetAsync(Guid userId, Guid runtimeId, CancellationToken cancellationToken, decimal? monthlyBudget = null, IReadOnlyList<string>? allowedModels = null, RuntimeCredentialPurpose purpose = RuntimeCredentialPurpose.Agent)
    {
        var settings = options.Value;
        var cacheKey = (userId, purpose);
        var models = (allowedModels ?? settings.AllowedModels.ToList()).ToArray();
        if (models.Length == 0 || models.Any(m => !settings.AllowedModels.Contains(m)))
        {
            throw new ModelCredentialException("No permitted model set is available.");
        }
        if (TryGetValid(cacheKey, settings, models) is { } cached)
        {
            return cached;
        }

        // 同一使用者與用途同時只發一把，避免並行工作各自發 key。
        var userLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetValid(cacheKey, settings, models) is { } current)
            {
                return current;
            }

            var issued = await gateway.IssueRuntimeCredentialAsync(
                new RuntimeCredentialRequest(userId, runtimeId, models, settings.KeyLifetime, settings.MaxBudget, monthlyBudget),
                cancellationToken).ConfigureAwait(false);

            if (_credentials.TryGetValue(cacheKey, out var previous))
            {
                await RevokeQuietlyAsync(previous.Credential, cancellationToken).ConfigureAwait(false);
            }

            _credentials[cacheKey] = new CachedCredential(issued, models);
            logger.LogInformation("Issued model key {KeyId} for user {UserId}, expires {ExpiresAt:O}", issued.KeyId, userId, issued.ExpiresAt);
            return issued;
        }
        finally
        {
            userLock.Release();
        }
    }

    /// <summary>撤銷使用者目前的 key（例如帳號停用時，ADR-0004）。</summary>
    public async Task RevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var key in _credentials.Keys.Where(key => key.UserId == userId))
        {
            if (_credentials.TryRemove(key, out var credential))
                await RevokeQuietlyAsync(credential.Credential, cancellationToken).ConfigureAwait(false);
        }
    }

    private RuntimeModelCredential? TryGetValid((Guid UserId, RuntimeCredentialPurpose Purpose) cacheKey, ModelCredentialOptions settings, IReadOnlyList<string> models) =>
        _credentials.TryGetValue(cacheKey, out var credential) && credential.Credential.ExpiresAt - timeProvider.GetUtcNow() > settings.RenewBefore
            && credential.Models.ToHashSet(StringComparer.Ordinal).SetEquals(models)
            ? credential.Credential
            : null;

    private async Task RevokeQuietlyAsync(RuntimeModelCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            await gateway.RevokeRuntimeCredentialAsync(credential.KeyId, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 撤銷失敗不影響執行：舊 key 仍會在有效期後失效，只記錄警告。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to revoke model key {KeyId}", credential.KeyId);
        }
    }
}
