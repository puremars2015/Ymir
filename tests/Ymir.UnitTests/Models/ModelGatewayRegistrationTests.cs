using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Infrastructure;
using Ymir.VibeMaker.Infrastructure.LiteLlm;

namespace Ymir.UnitTests.Models;

/// <summary>ADR-0004：非 Development 必須使用 LiteLLM virtual key，不能退回共用的開發 key。</summary>
public class ModelGatewayRegistrationTests
{
    private static ServiceProvider Build(bool isDevelopment, params (string Key, string Value)[] settings)
    {
        (string Key, string Value)[] all = [("VibeMaker:Runtime:Provider", "Podman"), ("VibeMaker:Pi:ModelId", "minimax"), .. settings];
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(all.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddVibeMakerAgentRuntime(configuration, isDevelopment);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Production_WithoutLiteLlm_RefusesToStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(isDevelopment: false));
        Assert.Contains("VibeMaker:LiteLlm", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterKeyWithoutBaseUrl_RefusesToStart()
    {
        Assert.Throws<InvalidOperationException>(() => Build(isDevelopment: false, ("VibeMaker:LiteLlm:MasterKey", "sk-master")));
    }

    [Fact]
    public void Development_WithoutLiteLlm_UsesDevelopmentKey()
    {
        using var provider = Build(isDevelopment: true);
        Assert.IsType<DevelopmentModelGateway>(provider.GetRequiredService<IModelGateway>());
    }

    [Fact]
    public void Configured_UsesLiteLlm_AndRestrictsModelsToPiModelByDefault()
    {
        using var provider = Build(isDevelopment: false, ("VibeMaker:LiteLlm:MasterKey", "sk-master"), ("VibeMaker:LiteLlm:BaseUrl", "http://127.0.0.1:4000"));

        Assert.IsType<LiteLlmModelGateway>(provider.GetRequiredService<IModelGateway>());
        Assert.Equal(["minimax"], provider.GetRequiredService<IOptions<ModelCredentialOptions>>().Value.AllowedModels);
    }

    [Fact]
    public void ExplicitAllowedModels_AreKept()
    {
        using var provider = Build(
            isDevelopment: false,
            ("VibeMaker:LiteLlm:MasterKey", "sk-master"),
            ("VibeMaker:LiteLlm:BaseUrl", "http://127.0.0.1:4000"),
            ("VibeMaker:LiteLlm:AllowedModels:0", "minimax"),
            ("VibeMaker:LiteLlm:AllowedModels:1", "fake-model"));

        Assert.Equal(["minimax", "fake-model"], provider.GetRequiredService<IOptions<ModelCredentialOptions>>().Value.AllowedModels);
    }
}
