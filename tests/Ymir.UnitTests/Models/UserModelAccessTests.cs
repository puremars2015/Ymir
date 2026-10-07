using Microsoft.Extensions.Options;
using Ymir.Platform.Settings;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Models;

public sealed class UserModelAccessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static ModelAccessService Create(MemoryStore store, string[]? ids = null)
    {
        ids ??= ["a", "b", "c", "d"];
        var options = new ModelCredentialOptions();
        foreach (var id in ids) options.AllowedModels.Add(id);
        return new(new ModelCatalog(ids.Select(id => new ModelDescriptor(id, id)).ToList(), "a"), Options.Create(options), store);
    }

    [Fact]
    public async Task InheritsSystem_AndOverridesOnlySpecifiedModels_ForOneUser()
    {
        var store = new MemoryStore();
        var service = Create(store);
        await service.SaveAsync(new(["a", "b", "d"], "d"), "admin", Ct);
        Assert.Equal(["a", "b", "d"], (await service.GetForUserAsync(Alice, Ct)).Models.Select(m => m.Id));
        await service.SaveUserOverridesAsync(Alice, new Dictionary<string, bool> { ["c"] = true, ["d"] = false }, "admin", Ct);
        var state = await service.GetForUserAsync(Alice, Ct);
        Assert.Equal(["a", "b", "c"], state.Models.Select(m => m.Id));
        Assert.Equal("a", state.DefaultModelId);
        Assert.Equal(["a", "b", "d"], (await service.GetForUserAsync(Bob, Ct)).Models.Select(m => m.Id));
        await service.SaveAsync(new(["b", "d"], "b"), "admin", Ct);
        Assert.Equal(["b", "c"], (await service.GetForUserAsync(Alice, Ct)).Models.Select(m => m.Id));
        Assert.Equal("b", (await Create(store).GetForUserAsync(Alice, Ct)).DefaultModelId);
        Assert.Equal(["b", "d"], (await service.ResetUserOverridesAsync(Alice, Ct)).Effective.Models.Select(m => m.Id));
    }

    [Fact]
    public async Task DeploymentRemoval_OverridesCannotBypassDeploymentAllowlist()
    {
        var store = new MemoryStore();
        var service = Create(store);
        await service.SaveUserOverridesAsync(Alice, new Dictionary<string, bool> { ["c"] = true }, "admin", Ct);
        var restarted = Create(store, ["a", "b"]);
        Assert.False((await restarted.GetForUserAsync(Alice, Ct)).IsAvailable("c"));
        await Assert.ThrowsAsync<ArgumentException>(() => restarted.SaveUserOverridesAsync(Alice, new Dictionary<string, bool> { ["c"] = true }, "admin", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveUserOverridesAsync(Alice, new Dictionary<string, bool> { ["unknown"] = true }, "admin", Ct));
    }

    [Fact]
    public async Task DenyAll_ReturnsNoDefault_EmptyOverridesRestoreInheritance()
    {
        var service = Create(new MemoryStore());
        var state = await service.SaveUserOverridesAsync(Alice, new Dictionary<string, bool> { ["a"] = false, ["b"] = false, ["c"] = false, ["d"] = false }, "admin", Ct);
        Assert.Empty(state.Effective.Models);
        Assert.Null(state.Effective.DefaultModelId);
        Assert.Equal(4, (await service.SaveUserOverridesAsync(Alice, new Dictionary<string, bool>(), "admin", Ct)).Effective.Models.Count);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{\"a\":\"allow\"}")]
    public async Task CorruptUserPolicy_FailsClosed(string value)
    {
        var store = new MemoryStore();
        await store.SetAsync("vibemaker.user_model_access." + Alice.ToString("D"), value, "admin", Ct);
        var state = await Create(store).GetUserStateAsync(Alice, Ct);
        Assert.False(state.IsValid);
        Assert.Empty(state.Effective.Models);
    }

    private sealed class MemoryStore : ISystemSettingsStore
    {
        private readonly Dictionary<string, SystemSettingValue> _values = [];
        public Task<SystemSettingValue?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, string updatedBy, CancellationToken cancellationToken) { _values[key] = new(value, DateTimeOffset.UtcNow, updatedBy); return Task.CompletedTask; }
        public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) { foreach (var key in keys) _values.Remove(key); return Task.CompletedTask; }
        public Task<SystemSettingValue?> GetSecretAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetSecretAsync(string key, string plaintext, string updatedBy, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
