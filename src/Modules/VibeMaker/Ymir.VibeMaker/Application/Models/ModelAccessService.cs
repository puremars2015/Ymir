using System.Text.Json;
using Microsoft.Extensions.Options;
using Ymir.Platform.Settings;

namespace Ymir.VibeMaker.Application.Models;

public sealed record ModelAccessSettings(IReadOnlyList<string> EnabledModelIds, string DefaultModelId);
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
}
