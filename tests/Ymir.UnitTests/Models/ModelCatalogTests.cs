using Microsoft.Extensions.Configuration;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Infrastructure;

namespace Ymir.UnitTests.Models;

public class ModelCatalogTests
{
    [Fact]
    public void ThinkingCapability_UsesExactConfiguredLevelsAndRejectsUnsupportedAdapters()
    {
        var catalog = FromConfig(("VibeMaker:Models:0:Id", "luna"),
            ("VibeMaker:Pi:ModelId", "luna"),
            ("VibeMaker:Models:0:Thinking:Parameter", "reasoning_effort"),
            ("VibeMaker:Models:0:Thinking:Levels:0", "none"),
            ("VibeMaker:Models:0:Thinking:Levels:1", "max"));
        var model = Assert.Single(catalog.Models);
        Assert.True(model.AcceptsThinking("none"));
        Assert.True(model.AcceptsThinking("max"));
        Assert.True(model.AcceptsThinking(null));
        Assert.False(model.AcceptsThinking("low"));
        Assert.Throws<InvalidOperationException>(() => FromConfig(
            ("VibeMaker:Models:0:Id", "budget"),
            ("VibeMaker:Models:0:Thinking:Parameter", "thinking.budget_tokens"),
            ("VibeMaker:Models:0:Thinking:Levels:0", "high")));
        Assert.Throws<InvalidOperationException>(() => FromConfig(
            ("VibeMaker:Models:0:Id", "mandatory"),
            ("VibeMaker:Models:0:Thinking:Parameter", "reasoning_effort"),
            ("VibeMaker:Models:0:Thinking:Required", "true"),
            ("VibeMaker:Models:0:Thinking:Levels:0", "none")));
    }
    private static ModelCatalog FromConfig(params (string Key, string Value)[] settings) =>
        VibeMakerInfrastructureExtensions.BuildModelCatalog(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build());

    [Fact]
    public void NoModelList_ContainsOnlyThePiDefaultModel()
    {
        var catalog = FromConfig(("VibeMaker:Pi:ModelId", "minimax"));

        var model = Assert.Single(catalog.Models);
        Assert.Equal("minimax", model.Id);
        Assert.Equal("minimax", catalog.DefaultModelId);
    }

    [Fact]
    public void ModelList_IsReadWithDisplayNames_AndDefaultIsAddedWhenMissing()
    {
        var catalog = FromConfig(
            ("VibeMaker:Pi:ModelId", "minimax"),
            ("VibeMaker:Models:0:Id", "gpt-x"),
            ("VibeMaker:Models:0:DisplayName", "GPT X"),
            ("VibeMaker:Models:1:Id", "gpt-x"),
            ("VibeMaker:Models:2:Id", "  "));

        Assert.Equal(["minimax", "gpt-x"], catalog.Models.Select(m => m.Id));
        Assert.Equal("GPT X", catalog.Models[1].DisplayName);
    }

    [Fact]
    public void Resolve_FallsBackToDefault_ForUnknownOrMissingModels()
    {
        var catalog = new ModelCatalog([new ModelDescriptor("a", "A"), new ModelDescriptor("b", "B")], "a");

        Assert.Equal("b", catalog.Resolve("b"));
        Assert.Equal("a", catalog.Resolve("removed-model"));
        Assert.Equal("a", catalog.Resolve(null));
        Assert.False(catalog.IsAvailable("removed-model"));
    }

    [Fact]
    public void DefaultMustBeInTheList()
    {
        Assert.Throws<ArgumentException>(() => new ModelCatalog([new ModelDescriptor("a", "A")], "b"));
    }
}
