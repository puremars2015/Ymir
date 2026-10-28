using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Infrastructure;
using Ymir.VibeMaker.Infrastructure.Health;
using Ymir.VibeMaker.Infrastructure.Knowledge;
using Ymir.VibeMaker.Infrastructure.LiteLlm;

namespace Ymir.UnitTests.Models;

/// <summary>ADR-0004 補充：LiteLLM 管理端點放在 Cloudflare Access 後面時，service token 只送往 BaseUrl 同一來源、不跟隨 redirect、不寫進 log。</summary>
public class CloudflareAccessTests
{
    private const string ClientId = "test-client.access";
    private const string ClientSecret = "cfast_test-secret-value";
    private const string BaseUrl = "https://llm-admin.example.com";

    private static async Task<HttpRequestMessage> SendThroughHandlerAsync(string target, LiteLlmOptions options)
    {
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json("{}"));
        using var client = new HttpClient(new CloudflareAccessHandler(Options.Create(options)) { InnerHandler = stub });
        using var response = await client.GetAsync(new Uri(target), TestContext.Current.CancellationToken);
        return Assert.Single(stub.Requests).Request;
    }

    private static LiteLlmOptions Configured() => new()
    {
        BaseUrl = new Uri(BaseUrl),
        MasterKey = "sk-master",
        CfAccessClientId = ClientId,
        CfAccessClientSecret = ClientSecret,
    };

    [Fact]
    public async Task SameOrigin_GetsServiceTokenHeaders()
    {
        var request = await SendThroughHandlerAsync($"{BaseUrl}/user/info?user_id=x", Configured());

        Assert.Equal(ClientId, Assert.Single(request.Headers.GetValues(CloudflareAccessHandler.ClientIdHeader)));
        Assert.Equal(ClientSecret, Assert.Single(request.Headers.GetValues(CloudflareAccessHandler.ClientSecretHeader)));
    }

    [Theory]
    [InlineData("https://llm.example.com/v1/embeddings")] // 其他主機（例如知識庫另設的位址）
    [InlineData("https://llm-admin.example.com:8443/user/info")] // 不同 port
    [InlineData("http://llm-admin.example.com/user/info")] // 不同 scheme
    public async Task OtherOrigin_NeverGetsServiceToken(string target)
    {
        var request = await SendThroughHandlerAsync(target, Configured());

        Assert.False(request.Headers.Contains(CloudflareAccessHandler.ClientIdHeader));
        Assert.False(request.Headers.Contains(CloudflareAccessHandler.ClientSecretHeader));
    }

    [Fact]
    public async Task NotConfigured_SendsNoHeaders()
    {
        var request = await SendThroughHandlerAsync($"{BaseUrl}/user/info", new LiteLlmOptions { BaseUrl = new Uri(BaseUrl), MasterKey = "sk-master" });

        Assert.False(request.Headers.Contains(CloudflareAccessHandler.ClientIdHeader));
        Assert.False(request.Headers.Contains(CloudflareAccessHandler.ClientSecretHeader));
    }

    private static ServiceProvider Build(ILoggerProvider? logs, params (string Key, string Value)[] settings)
    {
        (string Key, string Value)[] all = [("VibeMaker:Runtime:Provider", "Podman"), ("VibeMaker:Pi:ModelId", "minimax"), .. settings];
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(all.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value)))
            .Build();
        var services = new ServiceCollection();
        if (logs is null)
        {
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        }
        else
        {
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        }

        // 完整的模組註冊：知識庫與 health check 的 client 註冊在 AddVibeMakerInfrastructure（只註冊，不連資料庫）。
        services.AddVibeMakerInfrastructure(configuration, "Server=unused;Database=unused", isDevelopment: false);
        return services.BuildServiceProvider();
    }

    private static (string, string)[] AccessSettings(string baseUrl = BaseUrl) =>
    [
        ("VibeMaker:LiteLlm:MasterKey", "sk-master"),
        ("VibeMaker:LiteLlm:BaseUrl", baseUrl),
        ("VibeMaker:LiteLlm:CfAccessClientId", ClientId),
        ("VibeMaker:LiteLlm:CfAccessClientSecret", ClientSecret),
    ];

    [Theory]
    [InlineData("VibeMaker:LiteLlm:CfAccessClientId", ClientId)]
    [InlineData("VibeMaker:LiteLlm:CfAccessClientSecret", ClientSecret)]
    public void OnlyHalfOfServiceToken_RefusesToStart(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(null, ("VibeMaker:LiteLlm:MasterKey", "sk-master"), ("VibeMaker:LiteLlm:BaseUrl", BaseUrl), (key, value)));
        Assert.Contains("CfAccessClientId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceTokenOverPlainHttp_RefusesToStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(null, AccessSettings("http://llm-admin.example.com")));
        Assert.Contains("https", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LiteLlmModelGateway.HttpClientName)]
    [InlineData(nameof(LiteLlmHealthCheck))]
    [InlineData(LiteLlmEmbeddingClient.HttpClientName)]
    [InlineData(LiteLlmAnswerClient.HttpClientName)]
    public void LiteLlmClients_AttachServiceToken_AndDoNotFollowRedirects(string clientName)
    {
        using var provider = Build(null, AccessSettings());

        var handlers = new List<HttpMessageHandler>();
        for (HttpMessageHandler? current = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName); current is not null;
             current = (current as DelegatingHandler)?.InnerHandler)
        {
            handlers.Add(current);
        }

        // .NET 跟隨 redirect 時只移除 Authorization，service token 會被帶到新的位址，所以這些 client 一律不跟隨。
        Assert.Contains(handlers, h => h is CloudflareAccessHandler);
        Assert.False(Assert.IsType<SocketsHttpHandler>(handlers[^1]).AllowAutoRedirect);
    }

    [Fact]
    public async Task ServiceTokenAndMasterKey_NeverAppearInHttpClientLogs()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Build(logs, AccessSettings());

        // 換掉最底層的連線，保留 Ymir 註冊的 handler 與 HttpClientFactory 的 logging handler。
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(LiteLlmModelGateway.HttpClientName);
        var stub = new StubHttpHandler((_, _) => StubHttpHandler.Json("{}"));
        options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = stub);

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(LiteLlmModelGateway.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/user/info");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sk-master");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(ClientSecret, Assert.Single(Assert.Single(stub.Requests).Request.Headers.GetValues(CloudflareAccessHandler.ClientSecretHeader)));
        // header 有被記錄（Trace），但值一律遮蔽。
        Assert.Contains(logs.Messages, m => m.Contains(CloudflareAccessHandler.ClientSecretHeader, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(ClientSecret, StringComparison.Ordinal) || m.Contains("sk-master", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages => messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
