using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Models;

/// <summary>模型名稱與 provider 分開：管理介面顯示 provider，使用者只看模型名稱（ADR-0020 補充）。</summary>
public class ModelNamesTests
{
    [Theory]
    [InlineData("MiniMax M2.7", "minimax.io", "MiniMax M2.7", "minimax.io")]
    [InlineData("GPT-6.1 Sol (ChatGPT OAuth)", null, "GPT-6.1 Sol", "ChatGPT OAuth")]
    [InlineData("Claude Sonnet 5.5（OpenRouter）", "", "Claude Sonnet 5.5", "OpenRouter")]
    [InlineData("  GPT X ( OpenRouter )  ", "  ", "GPT X", "OpenRouter")]
    [InlineData("GPT X (old)", "OpenRouter", "GPT X (old)", "OpenRouter")]
    [InlineData("MiniMax M3", null, "MiniMax M3", null)]
    [InlineData("(beta)", null, "(beta)", null)]
    [InlineData("GPT X ()", null, "GPT X ()", null)]
    [InlineData("GPT X (a", null, "GPT X (a", null)]
    public void Normalize_UsesTheConfiguredProvider_OrSplitsALegacySuffix(string displayName, string? provider, string expectedName, string? expectedProvider)
    {
        Assert.Equal((expectedName, expectedProvider), ModelNames.Normalize("id", displayName, provider));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", "minimax.io")]
    public void Normalize_FallsBackToTheId_WhenThereIsNoDisplayName(string? displayName, string? provider)
    {
        Assert.Equal(("minimax", provider?.Trim() is { Length: > 0 } p ? p : null), ModelNames.Normalize("minimax", displayName, provider));
    }

    [Fact]
    public void ForUsers_ShowsOnlyTheModelName_AndAddsTheProviderOnlyForDuplicateNames()
    {
        var names = ModelNames.ForUsers(
        [
            new ModelDescriptor("minimax", "MiniMax M2.7", Provider: "minimax.io"),
            new ModelDescriptor("openrouter-sol", "GPT-6.1 Sol", Provider: "OpenRouter"),
            new ModelDescriptor("chatgpt-sol", "gpt-6.1 sol", Provider: "ChatGPT OAuth"),
            new ModelDescriptor("legacy", "Legacy"),
            new ModelDescriptor("legacy-2", "Legacy"),
        ]);

        // 同名但沒有 provider 的模型無從區分，維持原名
        Assert.Equal(["MiniMax M2.7", "GPT-6.1 Sol (OpenRouter)", "gpt-6.1 sol (ChatGPT OAuth)", "Legacy", "Legacy"], names);
    }
}
