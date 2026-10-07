using Microsoft.Extensions.Logging.Abstractions;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Models;

public class ModelBudgetGuardTests
{
    private sealed class BudgetGateway : IModelGateway
    {
        public ModelBudgetStatus? Status { get; set; }

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public bool SupportsUsage => true;

        public Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ApplyUserBudgetAsync(Guid userId, decimal? monthlyBudget, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ModelBudgetStatus?> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? Task.FromException<ModelBudgetStatus?>(new ModelCredentialException("down")) : Task.FromResult(Status);
        }

        public Task<IReadOnlyDictionary<Guid, ModelUserUsage>> GetUsageAsync(IReadOnlyCollection<Guid> userIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task ReportsExceededBudget_AndCachesForAMinute()
    {
        var gateway = new BudgetGateway { Status = new ModelBudgetStatus(10m, 10m, null) };
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var guard = new ModelBudgetGuard(gateway, time, NullLogger<ModelBudgetGuard>.Instance);
        var ct = TestContext.Current.CancellationToken;

        Assert.NotNull(await guard.FindExceededAsync(User, 10m, ct));
        gateway.Status = new ModelBudgetStatus(0m, 50m, null); // 管理員調高預算
        Assert.NotNull(await guard.FindExceededAsync(User, 50m, ct)); // 仍在快取內
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(await guard.FindExceededAsync(User, 50m, ct));
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task DoesNotBlock_WithoutBudget_OrWhenLiteLlmIsDown()
    {
        var gateway = new BudgetGateway { Fail = true };
        var guard = new ModelBudgetGuard(gateway, TimeProvider.System, NullLogger<ModelBudgetGuard>.Instance);
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await guard.FindExceededAsync(User, null, ct));
        Assert.Equal(0, gateway.Calls);
        Assert.Null(await guard.FindExceededAsync(User, 10m, ct));
    }
}
