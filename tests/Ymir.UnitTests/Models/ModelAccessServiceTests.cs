using Microsoft.Extensions.Options;
using Ymir.Platform.Settings;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Models;

public class ModelAccessServiceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static (ModelAccessService Service, MemoryStore Store) Create()
    {
        var options = new ModelCredentialOptions();
        options.AllowedModels.Add("one"); options.AllowedModels.Add("two");
        var store = new MemoryStore();
        return (new ModelAccessService(new ModelCatalog([new("one", "One"), new("two", "Two", true), new("not-permitted", "Other")], "one"), Options.Create(options), store), store);
    }

    [Fact]
    public async Task Save_PersistsAndRestrictsDeploymentModels_WithoutRestart()
    {
        var (service, store) = Create();
        Assert.Equal(["one", "two"], (await service.GetAsync(Ct)).Models.Select(m => m.Id));
        var saved = await service.SaveAsync(new(["two"], "two"), "admin", Ct);
        Assert.False(saved.IsAvailable("one"));
        Assert.Equal("two", saved.Resolve("one"));
        Assert.Equal("admin", store.Stored!.UpdatedBy);
        var (_, otherStore) = Create();
        otherStore.Stored = store.Stored;
        var options = new ModelCredentialOptions(); options.AllowedModels.Add("two");
        var restarted = new ModelAccessService(new ModelCatalog([new("two", "Two", true)], "two"), Options.Create(options), otherStore);
        Assert.Equal("two", (await restarted.GetAsync(Ct)).DefaultModelId);
        Assert.Null((await service.ResetAsync(Ct)).Stored);
        Assert.True((await service.GetAsync(Ct)).IsAvailable("one"));
    }

    [Theory]
    [InlineData("[]", "one")]
    [InlineData("[\"unknown\"]", "unknown")]
    [InlineData("[\"not-permitted\"]", "not-permitted")]
    [InlineData("[\"one\",\"one\"]", "one")]
    [InlineData("[\"one\"]", "two")]
    public async Task InvalidPolicy_IsRejectedWithoutWriting(string ids, string defaultId)
    {
        var (service, store) = Create();
        var settings = new ModelAccessSettings(System.Text.Json.JsonSerializer.Deserialize<string[]>(ids)!, defaultId);
        Assert.NotNull(service.Validate(settings));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(settings, "admin", Ct));
        Assert.Null(store.Stored);
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("{\"enabledModelIds\":[\"removed\"],\"defaultModelId\":\"removed\"}")]
    [InlineData("{\"enabledModelIds\":null,\"defaultModelId\":\"one\"}")]
    public async Task CorruptOrObsoletePolicy_DoesNotReopenDeploymentModels(string json)
    {
        var (service, store) = Create();
        store.Stored = new(json, DateTimeOffset.UtcNow, "admin");
        var state = await service.GetAsync(Ct);
        Assert.Empty(state.Models);
        Assert.Null(state.Resolve("one"));
    }

    private sealed class MemoryStore : ISystemSettingsStore
    {
        public SystemSettingValue? Stored { get; set; }
        public Task<SystemSettingValue?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Stored);
        public Task SetAsync(string key, string value, string updatedBy, CancellationToken cancellationToken) { Stored = new(value, DateTimeOffset.UtcNow, updatedBy); return Task.CompletedTask; }
        public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) { Stored = null; return Task.CompletedTask; }
        public Task<SystemSettingValue?> GetSecretAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetSecretAsync(string key, string plaintext, string updatedBy, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}