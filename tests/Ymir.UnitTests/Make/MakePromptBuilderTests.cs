using Ymir.VibeMaker.Application.Make;
using Ymir.VibeMaker.Domain;

namespace Ymir.UnitTests.Make;

/// <summary><c>/make</c>：指令解析與送給 Agent 的指示。</summary>
public class MakePromptBuilderTests
{
    private static readonly MakeTopicPrompt Tool = new("小工具架設", "單頁小工具", "做成單一 HTML 檔");
    private static readonly MakeTopicPrompt Site = new("網站系統架設", null, "規劃前後端與資料結構");

    [Theory]
    [InlineData("/make", "")]
    [InlineData("  /MAKE  ", "")]
    [InlineData("/make 一個計算機", "一個計算機")]
    [InlineData("/Make\t倒數計時器 ", "倒數計時器")]
    [InlineData("/make\n多行\n描述", "多行\n描述")]
    public void ParseDescription_RecognizesTheCommand(string content, string expected)
    {
        Assert.Equal(expected, MakePromptBuilder.ParseDescription(content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("幫我 /make 一個東西")]
    [InlineData("/maker 一個東西")]
    [InlineData("/mak")]
    [InlineData("make 一個計算機")]
    public void ParseDescription_IgnoresOtherText(string? content)
    {
        Assert.Null(MakePromptBuilder.ParseDescription(content));
    }

    [Fact]
    public void ForTopic_IncludesTopicInstructions_AndAsksQuestionsFirst()
    {
        var prompt = MakePromptBuilder.ForTopic(Tool);

        Assert.Contains("小工具架設", prompt, StringComparison.Ordinal);
        Assert.Contains("單頁小工具", prompt, StringComparison.Ordinal);
        Assert.Contains("做成單一 HTML 檔", prompt, StringComparison.Ordinal);
        Assert.Contains("先不要建立任何檔案", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("使用者補充", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ForTopic_KeepsExtraDescription()
    {
        Assert.Contains("使用者補充：要有深色模式", MakePromptBuilder.ForTopic(Tool, "要有深色模式"), StringComparison.Ordinal);
    }

    [Fact]
    public void ForDescription_ListsTopics_AndAsksTheAgentToChoose()
    {
        var prompt = MakePromptBuilder.ForDescription("一個計算機", [Tool, Site]);

        Assert.Contains("使用者想建置：一個計算機", prompt, StringComparison.Ordinal);
        Assert.Contains("1. 小工具架設", prompt, StringComparison.Ordinal);
        Assert.Contains("2. 網站系統架設", prompt, StringComparison.Ordinal);
        Assert.Contains("規劃前後端與資料結構", prompt, StringComparison.Ordinal);
        Assert.Contains("主題：<名稱>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDescription_WithoutTopics_FallsBackToPlainBuild()
    {
        var prompt = MakePromptBuilder.ForDescription("一個計算機", []);

        Assert.Contains("一個計算機", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("可選的主題", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void MakeTopic_ValidatesFields()
    {
        var now = DateTimeOffset.UnixEpoch;

        Assert.ThrowsAny<Exception>(() => MakeTopic.Create(" ", null, "x", 0, true, now));
        Assert.ThrowsAny<Exception>(() => MakeTopic.Create(new string('名', MakeTopic.NameMaxLength + 1), null, "x", 0, true, now));
        Assert.ThrowsAny<Exception>(() => MakeTopic.Create("名稱", null, " ", 0, true, now));
        Assert.ThrowsAny<Exception>(() => MakeTopic.Create("名稱", new string('說', MakeTopic.DescriptionMaxLength + 1), "x", 0, true, now));
        var topic = MakeTopic.Create("  名稱 ", "  ", "指示", 3, false, now);
        Assert.Equal("名稱", topic.Name);
        Assert.Null(topic.Description);
        Assert.False(topic.IsEnabled);
    }
}
