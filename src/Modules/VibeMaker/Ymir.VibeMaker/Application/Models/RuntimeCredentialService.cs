using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ymir.VibeMaker.Application.Models;

/// <summary>
/// 每位使用者一把 virtual key（一個使用者一個 runtime，ADR-0007），快取在記憶體並在到期前換發（ADR-0004）。
/// key 不寫資料庫：API 重啟後重新發一把，舊 key 最晚在有效期後自動失效。
/// </summary>
public sealed class RuntimeCredentialService(
    IModelGateway gateway,
    IOptions<ModelCredentialOptions> options,
    TimeProvider timeProvider,
    ILogger<RuntimeCredentialService> logger)
{
    private readonly ConcurrentDictionary<Guid, RuntimeModelCredential> _credentials = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    /// <exception cref="ModelCredentialException">無法取得 key。</exception>
    public async Task<RuntimeModelCredential> GetAsync(Guid userId, Guid runtimeId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (TryGetValid(userId, settings) is { } cached)
        {
            return cached;
        }

        // 同一使用者同時只發一把，避免並行的 execution 各自發 key。
        var userLock = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetValid(userId, settings) is { } current)
            {
                return current;
            }

            var issued = await gateway.IssueRuntimeCredentialAsync(
                new RuntimeCredentialRequest(userId, runtimeId, settings.AllowedModels.ToList(), settings.KeyLifetime, settings.MaxBudget),
                cancellationToken).ConfigureAwait(false);

            if (_credentials.TryGetValue(userId, out var previous))
            {
                await RevokeQuietlyAsync(previous, cancellationToken).ConfigureAwait(false);
            }

            _credentials[userId] = issued;
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
        if (_credentials.TryRemove(userId, out var credential))
        {
            await RevokeQuietlyAsync(credential, cancellationToken).ConfigureAwait(false);
        }
    }

    private RuntimeModelCredential? TryGetValid(Guid userId, ModelCredentialOptions settings) =>
        _credentials.TryGetValue(userId, out var credential) && credential.ExpiresAt - timeProvider.GetUtcNow() > settings.RenewBefore
            ? credential
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
