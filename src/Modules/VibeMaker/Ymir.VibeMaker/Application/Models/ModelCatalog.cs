namespace Ymir.VibeMaker.Application.Models;

/// <param name="Id">LiteLLM 的 model_name，也是傳給 Pi <c>--model</c> 的值。</param>
/// <param name="SupportsImages">模型能直接看圖片（視覺輸入）；是的話使用者附加的圖片會一併附給模型，否則 Agent 只能用工具處理檔案。</param>
public sealed record ModelDescriptor(string Id, string DisplayName, bool SupportsImages = false);

/// <summary>
/// 可在對話中選用的模型（設定 <c>VibeMaker:Models</c>，由 Infrastructure 建立）。
/// 只有清單內的模型可以被選用；LiteLLM virtual key 也只允許這些模型（ADR-0004）。
/// </summary>
public sealed class ModelCatalog
{
    public ModelCatalog(IReadOnlyList<ModelDescriptor> models, string defaultModelId)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (!models.Any(m => m.Id == defaultModelId))
        {
            throw new ArgumentException($"Default model '{defaultModelId}' is not in the model list.", nameof(defaultModelId));
        }

        Models = models;
        DefaultModelId = defaultModelId;
    }

    public IReadOnlyList<ModelDescriptor> Models { get; }

    public string DefaultModelId { get; }

    public bool IsAvailable(string? modelId) => modelId is not null && Models.Any(m => m.Id == modelId);

    public bool SupportsImages(string? modelId) => Models.Any(m => m.Id == modelId && m.SupportsImages);

    /// <summary>選用的模型若已不在清單（例如設定被移除），退回預設模型，而不是讓執行失敗。</summary>
    public string Resolve(string? modelId) => IsAvailable(modelId) ? modelId! : DefaultModelId;
}
