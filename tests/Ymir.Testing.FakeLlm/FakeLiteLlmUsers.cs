using System.Collections.Concurrent;

namespace Ymir.Testing.FakeLlm;

/// <summary>
/// 模擬 LiteLLM 的使用者、預算與花費（<c>/user/*</c>、<c>/user/daily/activity</c>）。
/// 每次 chat completion 依 request 的 model 記 10 個輸入、5 個輸出 token，以 <see cref="CostPerRequest"/> 的假單價計費；
/// embeddings 也依 model 記錄（費用 0）。用量依 (UTC 日期, model) 累計，對應 LiteLLM 的 <c>breakdown.model_groups</c>（ADR-0021）。
/// 本期花費達到 <c>max_budget</c> 時拒絕呼叫（同 LiteLLM 的 budget exceeded）。
/// </summary>
public sealed class FakeLiteLlmUsers
{
    public const long PromptTokensPerRequest = 10;
    public const long CompletionTokensPerRequest = 5;

    /// <summary>每次呼叫的假費用（美元）。</summary>
    public const decimal CostPerRequest = 0.01m;

    private readonly ConcurrentDictionary<string, FakeLiteLlmUser> _users = new();

    public IReadOnlyDictionary<string, FakeLiteLlmUser> Users => _users;

    public FakeLiteLlmUser? Find(string? userId) => userId is not null && _users.TryGetValue(userId, out var user) ? user : null;

    public bool TryCreate(string userId, decimal? maxBudget, string? duration) =>
        _users.TryAdd(userId, new FakeLiteLlmUser(userId) { MaxBudget = maxBudget, BudgetDuration = duration });
}

/// <summary>某一天、某個模型的累計用量。</summary>
public readonly record struct FakeUsageCounters(long Requests, long PromptTokens, long CompletionTokens, decimal Spend);

public sealed class FakeLiteLlmUser(string userId)
{
    private readonly ConcurrentDictionary<(DateOnly Date, string Model), FakeUsageCounters> _daily = new();
    private readonly Lock _lock = new();

    public string UserId { get; } = userId;

    public decimal? MaxBudget { get; set; }

    public string? BudgetDuration { get; set; }

    /// <summary>本期花費（預算週期內）。</summary>
    public decimal Spend { get; private set; }

    public DateTimeOffset BudgetResetAt { get; } = DateTimeOffset.UtcNow.AddDays(30);

    public bool IsOverBudget => MaxBudget is { } max && Spend >= max;

    /// <summary>依 (UTC 日期, model) 累計的用量；model 為空字串表示 LiteLLM 沒有標示 model group 的紀錄。</summary>
    public IReadOnlyDictionary<(DateOnly Date, string Model), FakeUsageCounters> Daily => _daily;

    /// <summary>記一次請求；測試也可以直接呼叫來準備用量（例如過去的日期、沒有 model group 的紀錄）。</summary>
    /// <param name="model">request 的 model（LiteLLM 的 model group）；空字串表示沒有 model group。</param>
    /// <param name="date">UTC 日期；預設今天。</param>
    public void RecordRequest(
        string model,
        DateOnly? date = null,
        long promptTokens = FakeLiteLlmUsers.PromptTokensPerRequest,
        long completionTokens = FakeLiteLlmUsers.CompletionTokensPerRequest,
        decimal cost = FakeLiteLlmUsers.CostPerRequest)
    {
        ArgumentNullException.ThrowIfNull(model);
        lock (_lock)
        {
            Spend += cost;
            var added = new FakeUsageCounters(1, promptTokens, completionTokens, cost);
            _daily.AddOrUpdate(
                (date ?? DateOnly.FromDateTime(DateTime.UtcNow), model),
                added,
                (_, d) => new FakeUsageCounters(d.Requests + 1, d.PromptTokens + promptTokens, d.CompletionTokens + completionTokens, d.Spend + cost));
        }
    }
}
