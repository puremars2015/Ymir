using System.Collections.Concurrent;

namespace Ymir.Testing.FakeLlm;

/// <summary>
/// 模擬 LiteLLM 的使用者、預算與花費（<c>/user/*</c>、<c>/user/daily/activity</c>）。
/// 每次 chat completion 記 10 個輸入、5 個輸出 token，以 <see cref="CostPerRequest"/> 的假單價計費，
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

public sealed class FakeLiteLlmUser(string userId)
{
    private readonly ConcurrentDictionary<DateOnly, (long Requests, decimal Spend)> _daily = new();
    private readonly Lock _lock = new();

    public string UserId { get; } = userId;

    public decimal? MaxBudget { get; set; }

    public string? BudgetDuration { get; set; }

    /// <summary>本期花費（預算週期內）。</summary>
    public decimal Spend { get; private set; }

    public DateTimeOffset BudgetResetAt { get; } = DateTimeOffset.UtcNow.AddDays(30);

    public bool IsOverBudget => MaxBudget is { } max && Spend >= max;

    public IReadOnlyDictionary<DateOnly, (long Requests, decimal Spend)> Daily => _daily;

    public void RecordRequest()
    {
        lock (_lock)
        {
            Spend += FakeLiteLlmUsers.CostPerRequest;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            _daily.AddOrUpdate(today, (1, FakeLiteLlmUsers.CostPerRequest), (_, d) => (d.Requests + 1, d.Spend + FakeLiteLlmUsers.CostPerRequest));
        }
    }
}
