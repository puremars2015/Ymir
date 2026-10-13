using Ymir.VibeMaker.Application.Admin;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Admin;

/// <summary>ADR-0021：Ymir 的執行數與 LiteLLM 的 model group 用量以模型 id 合併成每人每模型的列。</summary>
public class ModelUsageBreakdownTests
{
    private const string Embedding = "fake-embedding";

    private static readonly ModelCatalog Catalog = new(
        [new ModelDescriptor("fake-model", "Fake"), new ModelDescriptor("fake-model-2", "Fake 2")],
        "fake-model");

    private static ModelUserUsage Gateway(params ModelGroupUsage[] models)
    {
        var total = models.Aggregate(ModelUsageMetrics.Zero, (sum, m) => sum.Add(m.Metrics));
        return Gateway(total, models);
    }

    private static ModelUserUsage Gateway(ModelUsageMetrics total, params ModelGroupUsage[] models) =>
        new(total.Spend, total.PromptTokens, total.CompletionTokens, total.Requests, null, models);

    [Fact]
    public void Merge_JoinsExecutionsAndLiteLlmByModelId()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount("fake-model", 3, 1)],
            Gateway(new ModelGroupUsage("fake-model", new(0.03m, 30, 15, 3))),
            Catalog,
            Embedding);

        var row = Assert.Single(rows);
        Assert.Equal(new UserModelUsage("fake-model", "Fake", ModelUsageKind.Catalog, 3, 1, new(0.03m, 30, 15, 3)), row);
    }

    [Fact]
    public void Merge_UsesCatalogDisplayNames_AndClassifiesEmbeddingAndUnlistedModels()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount("fake-model-2", 1, 0), new ModelExecutionCount("retired-model", 1, 0)],
            Gateway(new ModelGroupUsage(Embedding, new(0, 16, 0, 2))),
            Catalog,
            Embedding);

        Assert.Equal(("Fake 2", ModelUsageKind.Catalog), Describe(rows, "fake-model-2"));
        Assert.Equal((Embedding, ModelUsageKind.Embedding), Describe(rows, Embedding));
        Assert.Equal(("retired-model", ModelUsageKind.Unlisted), Describe(rows, "retired-model"));

        // 模型清單優先：embedding 模型若也列在清單內，以清單的名稱顯示。
        var listed = ModelUsageBreakdown.Merge([new ModelExecutionCount("fake-model", 1, 0)], null, Catalog, "fake-model");
        Assert.Equal(("Fake", ModelUsageKind.Catalog), Describe(listed, "fake-model"));

        static (string?, ModelUsageKind) Describe(IReadOnlyList<UserModelUsage> rows, string modelId)
        {
            var row = Assert.Single(rows, r => r.ModelId == modelId);
            return (row.DisplayName, row.Kind);
        }
    }

    [Fact]
    public void Merge_PutsLegacyExecutionsAndUnattributedRemainderInOneNullRow()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount(null, 2, 1), new ModelExecutionCount("fake-model", 1, 0)],
            // 總計比各 model group 多 1 次請求與 US$0.01；輸出 token 的總計反而比較少（LiteLLM 內部不一致）時差額限制為 0。
            Gateway(new ModelUsageMetrics(0.03m, 30, 9, 3), new ModelGroupUsage("fake-model", new(0.02m, 20, 10, 2))),
            Catalog,
            Embedding);

        Assert.Equal(2, rows.Count);
        var unattributed = rows[^1];
        Assert.Equal(new UserModelUsage(null, null, ModelUsageKind.Unattributed, 2, 1, new(0.01m, 10, 0, 1)), unattributed);
        Assert.Single(rows, r => r.ModelId is null);
    }

    [Fact]
    public void Merge_IgnoresSpendRemainderBelowTolerance()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount("fake-model", 1, 0)],
            Gateway(new ModelUsageMetrics(0.0200005m, 20, 10, 2), new ModelGroupUsage("fake-model", new(0.02m, 20, 10, 2))),
            Catalog,
            Embedding);

        // 差額 US$0.0000005 低於容差：不產生「未標示模型」那一列
        var row = Assert.Single(rows);
        Assert.Equal("fake-model", row.ModelId);

        var withLegacy = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount(null, 1, 0)],
            Gateway(new ModelUsageMetrics(0.0000005m, 0, 0, 0)),
            Catalog,
            Embedding);
        Assert.Equal(ModelUsageMetrics.Zero, Assert.Single(withLegacy).Metrics);
    }

    [Fact]
    public void Merge_WithoutGatewayData_LeavesMetricsNull()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount("fake-model", 2, 0), new ModelExecutionCount(null, 1, 1)],
            null,
            Catalog,
            Embedding);

        Assert.Equal(["fake-model", null], rows.Select(r => r.ModelId));
        Assert.All(rows, r => Assert.Null(r.Metrics));
        Assert.Equal(ModelUsageKind.Unattributed, rows[^1].Kind);
    }

    [Fact]
    public void Merge_ModelWithExecutionsButNoLiteLlmUsage_HasZeroMetrics()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount("fake-model-2", 1, 1)],
            Gateway(new ModelGroupUsage("fake-model", new(0.01m, 10, 5, 1))),
            Catalog,
            Embedding);

        var row = Assert.Single(rows, r => r.ModelId == "fake-model-2");
        Assert.Equal(ModelUsageMetrics.Zero, row.Metrics);
        Assert.Equal((1, 1), (row.Executions, row.Failed));
    }

    [Fact]
    public void Merge_OrdersBySpendThenRequestsThenExecutions_NullLast()
    {
        var rows = ModelUsageBreakdown.Merge(
            [
                new ModelExecutionCount(null, 9, 0),
                new ModelExecutionCount("e", 5, 0),
                new ModelExecutionCount("d", 1, 0),
                new ModelExecutionCount("c", 1, 0),
            ],
            Gateway(
                new ModelUsageMetrics(1m, 0, 0, 10),
                new ModelGroupUsage("a", new(0.5m, 0, 0, 1)),
                new ModelGroupUsage("b", new(0.1m, 0, 0, 5)),
                new ModelGroupUsage("f", new(0.1m, 0, 0, 2))),
            Catalog,
            Embedding);

        // a：費用最高；b、f 費用相同依請求數；c、d、e 都沒有 LiteLLM 用量，依執行數再依 id；未標示模型最後（儘管執行數最多）
        Assert.Equal(["a", "b", "f", "e", "c", "d", null], rows.Select(r => r.ModelId));
    }

    [Fact]
    public void Merge_DropsEmptyRows()
    {
        var rows = ModelUsageBreakdown.Merge(
            [new ModelExecutionCount("fake-model", 0, 0), new ModelExecutionCount(null, 0, 0)],
            Gateway(new ModelGroupUsage("fake-model-2", ModelUsageMetrics.Zero), new ModelGroupUsage(Embedding, new(0, 8, 0, 1))),
            Catalog,
            Embedding);

        // 沒有執行也沒有用量的列（含沒有差額的「未標示模型」）都不顯示
        var row = Assert.Single(rows);
        Assert.Equal(Embedding, row.ModelId);

        Assert.Empty(ModelUsageBreakdown.Merge([], null, Catalog, Embedding));
        Assert.Empty(ModelUsageBreakdown.Merge([], Gateway(), Catalog, Embedding));
    }

    [Fact]
    public void Merge_RowsSumToUserTotals()
    {
        ModelExecutionCount[] executions = [new("fake-model", 3, 1), new("fake-model-2", 1, 0), new(null, 2, 0)];
        var total = new ModelUsageMetrics(0.1234567m, 500, 250, 12);
        var gateway = Gateway(
            total,
            new ModelGroupUsage("fake-model", new(0.05m, 200, 100, 5)),
            new ModelGroupUsage("fake-model-2", new(0.0234567m, 100, 50, 3)),
            new ModelGroupUsage(Embedding, new(0, 40, 0, 2)));

        var rows = ModelUsageBreakdown.Merge(executions, gateway, Catalog, Embedding);

        Assert.Equal(executions.Sum(e => e.Executions), rows.Sum(r => r.Executions));
        Assert.Equal(executions.Sum(e => e.Failed), rows.Sum(r => r.Failed));
        Assert.Equal(total, rows.Aggregate(ModelUsageMetrics.Zero, (sum, r) => sum.Add(r.Metrics!)));
    }
}
