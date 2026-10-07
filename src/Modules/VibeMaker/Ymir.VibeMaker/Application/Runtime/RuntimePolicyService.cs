using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.Platform.Settings;
using Ymir.VibeMaker.Application.Executions;

namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>目前生效的執行政策（ADR-0011）。<see cref="TimeSpan.Zero"/> / 0 表示不限制。</summary>
/// <param name="MonthlyBudgetUsd">每人每月模型預算（美元），由 LiteLLM 強制（ADR-0004）；0 表示不限制。</param>
public sealed record RuntimePolicy(TimeSpan IdleTimeout, TimeSpan ExecutionTimeout, int MaxPendingExecutionsPerUser, int DailyExecutionLimit, decimal MonthlyBudgetUsd = 0)
{
    /// <summary>給模型入口的預算；0 → null（不限制）。</summary>
    public decimal? MonthlyBudget => MonthlyBudgetUsd > 0 ? MonthlyBudgetUsd : null;
}

/// <summary>管理介面編輯的值（分鐘 / 次數 / 美元）；存在 <c>platform.system_settings</c>（ADR-0010），不含機密。</summary>
/// <param name="MonthlyBudgetUsd">預設 0：較早儲存、沒有這個欄位的設定仍然有效（視為不限制）。</param>
public sealed record RuntimePolicySettings(int IdleTimeoutMinutes, int ExecutionTimeoutMinutes, int MaxPendingExecutionsPerUser, int DailyExecutionLimit, decimal MonthlyBudgetUsd = 0)
{
    public const int MaxIdleTimeoutMinutes = 24 * 60;
    public const int MaxExecutionTimeoutMinutes = 240;
    public const int MaxPendingLimit = 50;
    public const int MaxDailyLimit = 10_000;
    public const decimal MaxMonthlyBudgetUsd = 100_000;

    /// <summary>不合法時回傳給使用者看的訊息。</summary>
    public string? Validate() =>
        IdleTimeoutMinutes is < 0 or > MaxIdleTimeoutMinutes ? $"閒置停止時間必須在 0～{MaxIdleTimeoutMinutes} 分鐘之間（0 表示不自動停止）。"
        : ExecutionTimeoutMinutes is < 1 or > MaxExecutionTimeoutMinutes ? $"單次執行上限必須在 1～{MaxExecutionTimeoutMinutes} 分鐘之間。"
        : MaxPendingExecutionsPerUser is < 1 or > MaxPendingLimit ? $"每人同時排隊的工作數必須在 1～{MaxPendingLimit} 之間。"
        : DailyExecutionLimit is < 0 or > MaxDailyLimit ? $"每人每日執行次數必須在 0～{MaxDailyLimit} 之間（0 表示不限制）。"
        : MonthlyBudgetUsd is < 0 or > MaxMonthlyBudgetUsd || decimal.Round(MonthlyBudgetUsd, 2) != MonthlyBudgetUsd
            ? $"每人每月模型預算必須在 0～{MaxMonthlyBudgetUsd:0} 美元之間，最多兩位小數（0 表示不限制）。"
        : null;

    public RuntimePolicy ToPolicy() => new(
        TimeSpan.FromMinutes(IdleTimeoutMinutes),
        TimeSpan.FromMinutes(ExecutionTimeoutMinutes),
        MaxPendingExecutionsPerUser,
        DailyExecutionLimit,
        MonthlyBudgetUsd);
}

/// <param name="Stored">管理介面儲存的值（沒有則為 null，使用部署設定）。</param>
public sealed record RuntimePolicyState(RuntimePolicy Effective, RuntimePolicy Deployment, SystemSettingValue? Stored);

/// <summary>
/// 執行政策（ADR-0011）：閒置停止、單次執行逾時、每人排隊上限與每日次數。管理介面儲存的值優先於部署設定，
/// 不必重新啟動。每次送訊息與每個 execution 都會讀，所以快取 30 秒；本實例存檔後立即刷新。
/// 資料庫暫時無法使用或內容損毀時沿用上次的值（或部署設定），不讓執行失敗。
/// </summary>
public sealed partial class RuntimePolicyService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExecutionOptions> deployment,
    TimeProvider timeProvider,
    ILogger<RuntimePolicyService> logger) : IDisposable
{
    public const string Key = "vibemaker.runtime_policy";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private SystemSettingValue? _stored;
    private RuntimePolicySettings? _storedSettings;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public RuntimePolicy Deployment => new(
        deployment.Value.IdleTimeout,
        deployment.Value.Timeout,
        deployment.Value.MaxPendingExecutionsPerUser,
        deployment.Value.DailyExecutionLimit,
        deployment.Value.MonthlyBudgetUsd);

    public async Task<RuntimePolicy> GetAsync(CancellationToken cancellationToken) =>
        (await GetStateAsync(cancellationToken).ConfigureAwait(false)).Effective;

    public async Task<RuntimePolicyState> GetStateAsync(CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() - _loadedAt >= CacheDuration)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return new RuntimePolicyState(_storedSettings?.ToPolicy() ?? Deployment, Deployment, _stored);
    }

    public async Task<RuntimePolicyState> RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var stored = await scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>().GetAsync(Key, cancellationToken).ConfigureAwait(false);
                var settings = stored is null ? null : Parse(stored.Value);
                // 資料庫裡的值不合法（例如手動改壞）時忽略，退回部署設定。
                _stored = settings is null ? null : stored;
                _storedSettings = settings;
            }
        }
#pragma warning disable CA1031 // 資料庫暫時無法使用時沿用上次的值，不讓 execution 失敗。
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogLoadFailed(logger, ex);
        }
        finally
        {
            _loadedAt = timeProvider.GetUtcNow();
            _lock.Release();
        }

        return new RuntimePolicyState(_storedSettings?.ToPolicy() ?? Deployment, Deployment, _stored);
    }

    /// <summary>儲存管理介面的設定；呼叫端先以 <see cref="RuntimePolicySettings.Validate"/> 驗證。</summary>
    public async Task<RuntimePolicyState> SaveAsync(RuntimePolicySettings settings, string updatedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { } problem)
        {
            throw new ArgumentException(problem, nameof(settings));
        }

        var before = (await RefreshAsync(cancellationToken).ConfigureAwait(false)).Effective;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>()
                .SetAsync(Key, JsonSerializer.Serialize(settings, JsonOptions), updatedBy, cancellationToken).ConfigureAwait(false);
        }

        var state = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        await PropagateBudgetAsync(before, state.Effective, cancellationToken).ConfigureAwait(false);
        return state;
    }

    public async Task<RuntimePolicyState> ResetAsync(CancellationToken cancellationToken)
    {
        var before = (await RefreshAsync(cancellationToken).ConfigureAwait(false)).Effective;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await scope.ServiceProvider.GetRequiredService<ISystemSettingsStore>().DeleteAsync([Key], cancellationToken).ConfigureAwait(false);
        }

        var state = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        await PropagateBudgetAsync(before, state.Effective, cancellationToken).ConfigureAwait(false);
        return state;
    }

    /// <summary>
    /// 預算有變更時，套用到所有用過 runtime 的使用者（LiteLLM 的使用者預算）。best effort：失敗只記錄，
    /// 下一次發 key 時也會再套用一次（<see cref="Models.RuntimeCredentialService"/>）。
    /// </summary>
    private async Task PropagateBudgetAsync(RuntimePolicy before, RuntimePolicy after, CancellationToken cancellationToken)
    {
        if (before.MonthlyBudgetUsd == after.MonthlyBudgetUsd)
        {
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var gateway = scope.ServiceProvider.GetRequiredService<Models.IModelGateway>();
            if (!gateway.SupportsUsage)
            {
                return;
            }

            var userIds = await scope.ServiceProvider.GetRequiredService<Persistence.IVibeMakerDbContext>().AgentRuntimes
                .Select(r => r.UserId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
            var failed = 0;
            foreach (var userId in userIds)
            {
                try
                {
                    await gateway.ApplyUserBudgetAsync(userId, after.MonthlyBudget, cancellationToken).ConfigureAwait(false);
                }
                catch (Models.ModelCredentialException ex)
                {
                    failed++;
                    LogBudgetApplyFailed(logger, userId, ex);
                }
            }

            LogBudgetApplied(logger, userIds.Count - failed, userIds.Count, after.MonthlyBudgetUsd);
        }
    }

    public void Dispose() => _lock.Dispose();

    internal static RuntimePolicySettings? Parse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<RuntimePolicySettings>(json, JsonOptions);
            return settings?.Validate() is null ? settings : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to apply the monthly model budget to user {UserId}; it will be applied with the next key")]
    private static partial void LogBudgetApplyFailed(ILogger logger, Guid userId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied monthly model budget {Budget} to {Applied}/{Total} users")]
    private static partial void LogBudgetApplied(ILogger logger, int applied, int total, decimal budget);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load the runtime policy setting; keeping the previous value")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
