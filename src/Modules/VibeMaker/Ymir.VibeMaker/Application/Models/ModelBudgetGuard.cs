using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ymir.VibeMaker.Application.Models;

/// <summary>
/// 送訊息前檢查使用者本月的模型預算（ADR-0011）：預算已用完時直接告訴使用者，不必等 Agent 執行到一半才失敗。
/// 真正的強制在 LiteLLM（ADR-0004）；這裡只是提早提示，所以每位使用者快取 60 秒，LiteLLM 無法連線時不擋。
/// </summary>
public sealed partial class ModelBudgetGuard(IModelGateway gateway, TimeProvider timeProvider, ILogger<ModelBudgetGuard> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, (ModelBudgetStatus? Status, DateTimeOffset LoadedAt)> _cache = new();

    /// <returns>預算已用完時回傳目前狀態，否則 null。</returns>
    public async Task<ModelBudgetStatus?> FindExceededAsync(Guid userId, decimal? monthlyBudget, CancellationToken cancellationToken)
    {
        if (monthlyBudget is null || !gateway.SupportsUsage)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (!_cache.TryGetValue(userId, out var cached) || now - cached.LoadedAt >= CacheDuration)
        {
            try
            {
                cached = (await gateway.GetBudgetStatusAsync(userId, cancellationToken).ConfigureAwait(false), now);
                _cache[userId] = cached;
            }
            catch (ModelCredentialException ex)
            {
                LogCheckFailed(logger, userId, ex);
                return null;
            }
        }

        return cached.Status is { IsExceeded: true } status ? status : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to check the model budget of user {UserId}; not blocking (LiteLLM still enforces it)")]
    private static partial void LogCheckFailed(ILogger logger, Guid userId, Exception exception);
}
