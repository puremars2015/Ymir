using System.Text.Json;
using Microsoft.Extensions.Options;
using Ymir.Platform.Settings;

namespace Ymir.VibeMaker.Application.Models;

public sealed record ModelAccessSettings(IReadOnlyList<string> EnabledModelIds, string DefaultModelId);
public sealed record UserModelAccessState(ModelAccessState System, ModelAccessState Effective, IReadOnlyDictionary<string, bool> Overrides, bool IsValid);
public sealed record ModelAccessState(IReadOnlyList<ModelDescriptor> Models, string? DefaultModelId, SystemSettingValue? Stored)
{
    public bool IsAvailable(string? id) => id is not null && Models.Any(m => m.Id == id);
    public string? Resolve(string? id) => IsAvailable(id) ? id : DefaultModelId;
}

/// <summary>每次查詢與執行讀取持久設定，不快取權限；資料錯誤不自動重新開放模型。</summary>
public sealed class ModelAccessService(ModelCatalog catalog, IOptions<ModelCredentialOptions> credentials, ISystemSettingsStore store)
{
    public const string Key = "vibemaker.model_access";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<ModelDescriptor> DeploymentModels => catalog.Models.Where(m => credentials.Value.AllowedModels.Contains(m.Id)).ToList();
    public string? DeploymentDefault => DeploymentModels.Any(m => m.Id == catalog.DefaultModelId) ? catalog.DefaultModelId : DeploymentModels.Count > 0 ? DeploymentModels[0].Id : null;

    public async Task<ModelAccessState> GetAsync(CancellationToken ct)
    {
        var stored = await store.GetAsync(Key, ct).ConfigureAwait(false);
        if (stored is null) return new ModelAccessState(DeploymentModels, DeploymentDefault, null);
        ModelAccessSettings? settings;
        try { settings = JsonSerializer.Deserialize<ModelAccessSettings>(stored.Value, JsonOptions); }
        catch (JsonException) { settings = null; }
        var enabled = DeploymentModels.Where(m => settings?.EnabledModelIds?.Contains(m.Id) == true).ToList();
        var defaultId = enabled.Any(m => m.Id == settings?.DefaultModelId) ? settings!.DefaultModelId : enabled.FirstOrDefault()?.Id;
        return new ModelAccessState(enabled, defaultId, stored);
    }

    public string? Validate(ModelAccessSettings settings) =>
        settings.EnabledModelIds is null || settings.EnabledModelIds.Count == 0 ? "至少開放一個模型。"
        : settings.EnabledModelIds.Count != settings.EnabledModelIds.Distinct(StringComparer.Ordinal).Count() ? "模型不能重複。"
        : settings.EnabledModelIds.Any(id => !DeploymentModels.Any(m => m.Id == id)) ? "只能開放部署設定已接入及允許的模型。"
        : !settings.EnabledModelIds.Contains(settings.DefaultModelId) ? "預設模型必須是已開放的模型。" : null;

    public async Task<ModelAccessState> SaveAsync(ModelAccessSettings settings, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (Validate(settings) is { } problem) throw new ArgumentException(problem, nameof(settings));
        await store.SetAsync(Key, JsonSerializer.Serialize(settings, JsonOptions), actor, ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    public async Task<ModelAccessState> ResetAsync(CancellationToken ct)
    {
        await store.DeleteAsync([Key], ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    private static string UserKey(Guid userId) => "vibemaker.user_model_access." + userId.ToString("D");

    public async Task<ModelAccessState> GetForUserAsync(Guid userId, CancellationToken ct) =>
        (await GetUserStateAsync(userId, ct).ConfigureAwait(false)).Effective;

    public async Task<UserModelAccessState> GetUserStateAsync(Guid userId, CancellationToken ct)
    {
        var system = await GetAsync(ct).ConfigureAwait(false);
        var stored = await store.GetAsync(UserKey(userId), ct).ConfigureAwait(false);
        Dictionary<string, bool>? overrides = [];
        if (stored is not null)
        {
            try { overrides = JsonSerializer.Deserialize<Dictionary<string, bool>>(stored.Value, JsonOptions); }
            catch (JsonException) { overrides = null; }
        }
        // ADR-0019：損毀設定不得藉由繼承而重新開放權限；部署已移除的模型也不得重新出現。
        var enabled = DeploymentModels.Where(m => overrides is not null && (overrides.TryGetValue(m.Id, out var allow) ? allow : system.IsAvailable(m.Id))).ToList();
        var defaultId = enabled.Any(m => m.Id == system.DefaultModelId) ? system.DefaultModelId : enabled.FirstOrDefault()?.Id;
        return new(system, new(enabled, defaultId, stored), overrides ?? [], overrides is not null);
    }

    public string? ValidateUserOverrides(IReadOnlyDictionary<string, bool>? overrides) =>
        overrides is null ? "必須提供模型設定。"
        : overrides.Keys.Any(id => !DeploymentModels.Any(m => m.Id == id)) ? "只能設定部署已接入及允許的模型。" : null;

    public async Task<UserModelAccessState> SaveUserOverridesAsync(Guid userId, IReadOnlyDictionary<string, bool> overrides, string actor, CancellationToken ct)
    {
        if (ValidateUserOverrides(overrides) is { } problem) throw new ArgumentException(problem, nameof(overrides));
        if (overrides.Count == 0) return await ResetUserOverridesAsync(userId, ct).ConfigureAwait(false);
        await store.SetAsync(UserKey(userId), JsonSerializer.Serialize(overrides, JsonOptions), actor, ct).ConfigureAwait(false);
        return await GetUserStateAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task<UserModelAccessState> ResetUserOverridesAsync(Guid userId, CancellationToken ct)
    {
        await store.DeleteAsync([UserKey(userId)], ct).ConfigureAwait(false);
        return await GetUserStateAsync(userId, ct).ConfigureAwait(false);
    }
}
