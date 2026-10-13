using Ymir.VibeMaker.Application.Models;

namespace Ymir.VibeMaker.Application.Admin;

/// <summary>每人每模型用量的分類（ADR-0021）。</summary>
public enum ModelUsageKind
{
    /// <summary>模型清單（<c>VibeMaker:Models</c>）內的模型。</summary>
    Catalog,

    /// <summary>知識庫的 embedding 模型（<c>VibeMaker:Rag:EmbeddingModel</c>）。</summary>
    Embedding,

    /// <summary>不在模型清單（例如已移除、改名，或 LiteLLM 的 wildcard group）。</summary>
    Unlisted,

    /// <summary>未標示模型：舊執行沒有保存模型，或 LiteLLM 未標示 model group 的請求。</summary>
    Unattributed,
}

/// <param name="ModelId">執行時選用的模型；null 表示舊執行沒有保存模型。</param>
/// <param name="Failed">其中失敗的執行數。</param>
public sealed record ModelExecutionCount(string? ModelId, int Executions, int Failed);

/// <param name="ModelId">null 表示「未標示模型」那一列。</param>
/// <param name="DisplayName">模型清單的顯示名稱；不在清單時為模型 id；未標示模型為 null。</param>
/// <param name="Metrics">LiteLLM 的請求、token 與費用；null 表示沒有連接 LiteLLM 或這位使用者查詢失敗。</param>
public sealed record UserModelUsage(string? ModelId, string? DisplayName, ModelUsageKind Kind, int Executions, int Failed, ModelUsageMetrics? Metrics);

/// <summary>
/// 把 Ymir 的執行數（依 <c>AgentExecution.ModelId</c>）與 LiteLLM 的用量（依 model group）以模型 id 合併成每人每模型的列（ADR-0021）。
/// 純函式：Ymir 不保存 token 與費用（ADR-0004 補充），每次查詢時組合。
/// </summary>
public static class ModelUsageBreakdown
{
    /// <summary>總計與各模型加總的費用差額低於這個值時視為 0（浮點加總的誤差，不顯示成未標示模型）。</summary>
    public const decimal SpendTolerance = 0.000001m;

    /// <param name="executions">這位使用者依模型分組的執行數。</param>
    /// <param name="gateway">LiteLLM 的用量；null 表示沒有連接 LiteLLM 或這位使用者查詢失敗。</param>
    /// <param name="catalog">模型清單（顯示名稱與分類）。</param>
    /// <param name="embeddingModelId">知識庫的 embedding 模型；沒有設定時為 null。</param>
    public static IReadOnlyList<UserModelUsage> Merge(
        IReadOnlyList<ModelExecutionCount> executions,
        ModelUserUsage? gateway,
        ModelCatalog catalog,
        string? embeddingModelId)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(catalog);

        var groups = gateway?.Models ?? [];
        var keys = executions.Where(e => e.ModelId is not null).Select(e => e.ModelId!)
            .Union(groups.Select(g => g.ModelId), StringComparer.Ordinal);

        var rows = new List<UserModelUsage>();
        foreach (var modelId in keys)
        {
            var matching = executions.Where(e => string.Equals(e.ModelId, modelId, StringComparison.Ordinal)).ToList();
            var metrics = gateway is null
                ? null
                : groups.Where(g => string.Equals(g.ModelId, modelId, StringComparison.Ordinal))
                    .Aggregate(ModelUsageMetrics.Zero, (sum, g) => sum.Add(g.Metrics));
            var (kind, displayName) = Classify(modelId, catalog, embeddingModelId);
            var row = new UserModelUsage(modelId, displayName, kind, matching.Sum(e => e.Executions), matching.Sum(e => e.Failed), metrics);
            if (!IsEmpty(row))
            {
                rows.Add(row);
            }
        }

        rows = [.. rows
            .OrderByDescending(r => r.Metrics?.Spend ?? 0)
            .ThenByDescending(r => r.Metrics?.Requests ?? 0)
            .ThenByDescending(r => r.Executions)
            .ThenBy(r => r.ModelId, StringComparer.Ordinal)];

        // 未標示模型：舊執行（沒有保存模型）加上 LiteLLM 總計減去各 model group 的差額，讓各列加總等於使用者總計。
        var legacy = executions.Where(e => e.ModelId is null).ToList();
        var remainder = gateway is null ? null : Remainder(gateway, groups);
        var unattributed = new UserModelUsage(null, null, ModelUsageKind.Unattributed, legacy.Sum(e => e.Executions), legacy.Sum(e => e.Failed), remainder);
        if (!IsEmpty(unattributed))
        {
            rows.Add(unattributed);
        }

        return rows;
    }

    private static (ModelUsageKind Kind, string DisplayName) Classify(string modelId, ModelCatalog catalog, string? embeddingModelId)
    {
        // 模型清單優先：embedding 模型若也列在清單內，以清單的名稱顯示。
        if (catalog.Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.Ordinal)) is { } descriptor)
        {
            return (ModelUsageKind.Catalog, descriptor.DisplayName);
        }

        return string.Equals(modelId, embeddingModelId, StringComparison.Ordinal)
            ? (ModelUsageKind.Embedding, modelId)
            : (ModelUsageKind.Unlisted, modelId);
    }

    /// <summary>總計減去各模型加總，每個欄位不小於 0（LiteLLM 內部不一致時不顯示負數）；費用差額低於容差視為 0。</summary>
    private static ModelUsageMetrics Remainder(ModelUserUsage gateway, IReadOnlyList<ModelGroupUsage> groups)
    {
        var attributed = groups.Aggregate(ModelUsageMetrics.Zero, (sum, g) => sum.Add(g.Metrics));
        var spend = Math.Max(0, gateway.Spend - attributed.Spend);
        return new ModelUsageMetrics(
            spend < SpendTolerance ? 0 : spend,
            Math.Max(0, gateway.PromptTokens - attributed.PromptTokens),
            Math.Max(0, gateway.CompletionTokens - attributed.CompletionTokens),
            Math.Max(0, gateway.Requests - attributed.Requests));
    }

    /// <summary>沒有執行、也沒有任何 LiteLLM 用量的列不顯示（差額已限制不小於 0，所以未標示模型那一列同樣適用）。</summary>
    private static bool IsEmpty(UserModelUsage row) =>
        row.Executions == 0
        && (row.Metrics is null
            || (row.Metrics.Spend == 0 && row.Metrics.PromptTokens == 0 && row.Metrics.CompletionTokens == 0 && row.Metrics.Requests == 0));
}
