using Ymir.VibeMaker.Application.Make;

namespace Ymir.UnitTests.Make;

public class HelpPromptBuilderTests
{
    [Theory]
    [InlineData("/help", true)]
    [InlineData("  /HELP\n", true)]
    [InlineData("/helper", false)]
    [InlineData("/help 請建置", false)]
    [InlineData("幫我 /help", false)]
    [InlineData(null, false)]
    public void RecognizesOnlyTheHelpCommand(string? content, bool expected) =>
        Assert.Equal(expected, HelpPromptBuilder.IsHelpCommand(content));

    [Fact]
    public void ExplainsCapabilitiesCommandsAndEnabledTopicsWithoutInternalInstructions()
    {
        var prompt = HelpPromptBuilder.Build([new("建立公告 Word", "公司公告模板", "internal-template-path")]);
        Assert.Contains("先介紹用途，再列出指令", prompt, StringComparison.Ordinal);
        Assert.Contains("/help", prompt, StringComparison.Ordinal);
        Assert.Contains("/make", prompt, StringComparison.Ordinal);
        Assert.Contains("建立公告 Word：公司公告模板", prompt, StringComparison.Ordinal);
        Assert.Contains("不產生下載成果", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-template-path", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainsDescriptionModeWhenNoTopicsAreEnabled() =>
        Assert.Contains("目前沒有開放的建置主題", HelpPromptBuilder.Build([]), StringComparison.Ordinal);
}
