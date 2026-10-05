using System.Text.Json;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.UnitTests.PiAgent;

public class PiModelsConfigTests
{
    [Fact]
    public void Build_ReferencesApiKeyByEnvironmentVariable_NeverInline()
    {
        var options = new PiAgentOptions
        {
            ModelBaseUrl = new Uri("http://litellm:4000/v1/"),
            ProviderName = "ymir",
            ModelId = "gpt-x",
            DevelopmentApiKey = "super-secret",
        };

        var json = PiModelsConfig.Build(options, ["gpt-x", "gpt-y"]);

        Assert.DoesNotContain("super-secret", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        var provider = document.RootElement.GetProperty("providers").GetProperty("ymir");
        Assert.Equal("http://litellm:4000/v1", provider.GetProperty("baseUrl").GetString());
        Assert.Equal("openai-completions", provider.GetProperty("api").GetString());
        Assert.Equal("${LITELLM_API_KEY}", provider.GetProperty("apiKey").GetString());
        Assert.Equal(["gpt-x", "gpt-y"], provider.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("id").GetString()));
    }
}
