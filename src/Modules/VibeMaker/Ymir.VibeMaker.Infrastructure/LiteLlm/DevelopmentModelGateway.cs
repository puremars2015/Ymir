using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.VibeMaker.Infrastructure.LiteLlm;

/// <summary>
/// 開發用：沒有 LiteLLM 時直接回傳 <c>VibeMaker:Pi:DevelopmentApiKey</c>（給 Fake LLM）。
/// 只允許 Development 環境（DI 會在其他環境拒絕啟動，ADR-0004）。
/// </summary>
internal sealed class DevelopmentModelGateway(IOptions<PiAgentOptions> options) : IModelGateway
{
    public Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new RuntimeModelCredential("development", options.Value.DevelopmentApiKey ?? "dev-key", DateTimeOffset.MaxValue));

    public Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>沒有 LiteLLM：沒有預算也沒有用量，管理介面顯示「未連接 LiteLLM」。</summary>
    public bool SupportsUsage => false;

    public Task ApplyUserBudgetAsync(Guid userId, decimal? monthlyBudget, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ModelBudgetStatus?> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<ModelBudgetStatus?>(null);

    public Task<IReadOnlyDictionary<Guid, ModelUserUsage>> GetUsageAsync(IReadOnlyCollection<Guid> userIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ModelUserUsage>>(new Dictionary<Guid, ModelUserUsage>());
}
