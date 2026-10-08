using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Models;

/// <summary>ADR-0004：每位使用者一把 virtual key，快取在記憶體、到期前換發並撤銷舊的。</summary>
public class RuntimeCredentialServiceTests
{
    [Fact]
    public async Task KnowledgeKeys_DoNotReplaceAgentKey_AndDisableRevokesAllPurposes()
    {
        var (service, gateway, _) = Create(multipleModels: true);
        var ct = TestContext.Current.CancellationToken;
        var agent = await service.GetAsync(Alice, Guid.NewGuid(), ct, allowedModels: ["minimax"]);
        var embedding = await service.GetAsync(Alice, Guid.Empty, ct, allowedModels: ["gpt-x"], purpose: RuntimeCredentialPurpose.KnowledgeEmbedding);
        var answer = await service.GetAsync(Alice, Guid.Empty, ct, allowedModels: ["minimax"], purpose: RuntimeCredentialPurpose.KnowledgeAnswer);
        Assert.Empty(gateway.Revoked);
        Assert.Same(agent, await service.GetAsync(Alice, Guid.NewGuid(), ct, allowedModels: ["minimax"]));
        Assert.Equal(3, gateway.Issued.Count);
        await service.RevokeAsync(Alice, ct);
        Assert.Equal(new[] { agent.KeyId, embedding.KeyId, answer.KeyId }.Order(), gateway.Revoked.Order());
    }
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static (RuntimeCredentialService Service, CountingGateway Gateway, ManualTimeProvider Time) Create(bool multipleModels = false)
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));
        var gateway = new CountingGateway(time);
        var options = new ModelCredentialOptions { KeyLifetime = TimeSpan.FromHours(24), RenewBefore = TimeSpan.FromHours(1), MaxBudget = 3m };
        options.AllowedModels.Add("minimax");
        if (multipleModels) options.AllowedModels.Add("gpt-x");
        var service = new RuntimeCredentialService(gateway, Options.Create(options), time, NullLogger<RuntimeCredentialService>.Instance);
        return (service, gateway, time);
    }

    [Fact]
    public async Task ChangedModelPolicy_RotatesCachedKeyAndRevokesPrevious()
    {
        var (service, gateway, _) = Create(multipleModels: true);
        var ct = TestContext.Current.CancellationToken;
        var first = await service.GetAsync(Alice, Guid.NewGuid(), ct, allowedModels: ["minimax"]);
        var second = await service.GetAsync(Alice, Guid.NewGuid(), ct, allowedModels: ["gpt-x"]);
        Assert.NotEqual(first.KeyId, second.KeyId);
        Assert.Equal(["gpt-x"], gateway.Issued.Last().AllowedModels);
        Assert.Equal([first.KeyId], gateway.Revoked);
        await Assert.ThrowsAsync<ModelCredentialException>(() => service.GetAsync(Alice, Guid.NewGuid(), ct, allowedModels: []));
    }

    [Fact]
    public async Task SameUser_ReusesKeyUntilRenewalWindow()
    {
        var (service, gateway, time) = Create();
        var ct = TestContext.Current.CancellationToken;

        var first = await service.GetAsync(Alice, Guid.NewGuid(), ct);
        time.Advance(TimeSpan.FromHours(22));
        var second = await service.GetAsync(Alice, Guid.NewGuid(), ct);

        Assert.Same(first, second);
        var request = Assert.Single(gateway.Issued);
        Assert.Equal(["minimax"], request.AllowedModels);
        Assert.Equal(3m, request.MaxBudget);
    }

    [Fact]
    public async Task NearExpiry_IssuesNewKeyAndRevokesTheOldOne()
    {
        var (service, gateway, time) = Create();
        var ct = TestContext.Current.CancellationToken;

        var first = await service.GetAsync(Alice, Guid.NewGuid(), ct);
        time.Advance(TimeSpan.FromHours(23.5));
        var renewed = await service.GetAsync(Alice, Guid.NewGuid(), ct);

        Assert.NotEqual(first.ApiKey, renewed.ApiKey);
        Assert.Equal([first.KeyId], gateway.Revoked);
    }

    [Fact]
    public async Task DifferentUsers_GetDifferentKeys()
    {
        var (service, _, _) = Create();
        var ct = TestContext.Current.CancellationToken;

        var alice = await service.GetAsync(Alice, Guid.NewGuid(), ct);
        var bob = await service.GetAsync(Bob, Guid.NewGuid(), ct);

        Assert.NotEqual(alice.ApiKey, bob.ApiKey);
    }

    [Fact]
    public async Task ConcurrentRequests_ForSameUser_IssueOnlyOneKey()
    {
        var (service, gateway, _) = Create();
        var ct = TestContext.Current.CancellationToken;

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetAsync(Alice, Guid.NewGuid(), ct)));

        Assert.Single(gateway.Issued);
    }

    [Fact]
    public async Task RevokeFailure_DoesNotBlockRenewal()
    {
        var (service, gateway, time) = Create();
        var ct = TestContext.Current.CancellationToken;
        await service.GetAsync(Alice, Guid.NewGuid(), ct);
        gateway.FailRevoke = true;
        time.Advance(TimeSpan.FromHours(23.5));

        var renewed = await service.GetAsync(Alice, Guid.NewGuid(), ct);

        Assert.Equal("sk-key-2", renewed.ApiKey);
    }

    [Fact]
    public async Task Revoke_RemovesKeySoNextCallIssuesANewOne()
    {
        var (service, gateway, _) = Create();
        var ct = TestContext.Current.CancellationToken;
        var first = await service.GetAsync(Alice, Guid.NewGuid(), ct);

        await service.RevokeAsync(Alice, ct);
        var next = await service.GetAsync(Alice, Guid.NewGuid(), ct);

        Assert.Equal([first.KeyId], gateway.Revoked);
        Assert.NotEqual(first.ApiKey, next.ApiKey);
    }
}
