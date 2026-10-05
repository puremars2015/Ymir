using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Infrastructure.LiteLlm;

namespace Ymir.UnitTests.Models;

/// <summary>ADR-0004：以 LiteLLM key management API 發放 / 撤銷 virtual key。</summary>
public class LiteLlmModelGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    private const string MasterKey = "sk-master-secret";

    private static (LiteLlmModelGateway Gateway, StubHttpHandler Handler) Create(Func<HttpRequestMessage, string, HttpResponseMessage> respond, string? baseUrl = "http://litellm:4000")
    {
        var handler = new StubHttpHandler(respond);
        var options = Options.Create(new LiteLlmOptions { BaseUrl = baseUrl is null ? null : new Uri(baseUrl), MasterKey = MasterKey });
        var gateway = new LiteLlmModelGateway(new HttpClient(handler), options, new ManualTimeProvider(Now), NullLogger<LiteLlmModelGateway>.Instance);
        return (gateway, handler);
    }

    private static RuntimeCredentialRequest Request(decimal? budget = null) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), ["minimax"], TimeSpan.FromHours(24), budget);

    [Fact]
    public async Task Issue_SendsRestrictedKeyRequestWithMasterKey()
    {
        var (gateway, handler) = Create((_, _) => StubHttpHandler.Json("""{"key":"sk-virtual","token":"hashed","expires":"2026-10-06T00:00:00Z"}"""));

        var credential = await gateway.IssueRuntimeCredentialAsync(Request(budget: 5m), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://litellm:4000/key/generate", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(MasterKey, request.Headers.Authorization.Parameter);
        Assert.Equal(["minimax"], body!["models"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Equal("86400s", body["duration"]!.GetValue<string>());
        Assert.Equal(5m, body["max_budget"]!.GetValue<decimal>());
        Assert.Equal("11111111-1111-1111-1111-111111111111", body["metadata"]!["ymir_user_id"]!.GetValue<string>());
        Assert.StartsWith("ymir-user-11111111111111111111111111111111-", body["key_alias"]!.GetValue<string>(), StringComparison.Ordinal);

        Assert.Equal("sk-virtual", credential.ApiKey);
        Assert.Equal("hashed", credential.KeyId);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), credential.ExpiresAt);
    }

    [Fact]
    public async Task Issue_WithoutBudgetOrTokenOrExpiry_FallsBackSafely()
    {
        var (gateway, handler) = Create((_, _) => StubHttpHandler.Json("""{"key":"sk-virtual"}"""));

        var credential = await gateway.IssueRuntimeCredentialAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(handler.Requests[0].Body!.ContainsKey("max_budget"));
        Assert.Equal("sk-virtual", credential.KeyId);
        Assert.Equal(Now.AddHours(24), credential.ExpiresAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Issue_FailureStatus_ThrowsModelCredentialException(HttpStatusCode status)
    {
        var (gateway, _) = Create((_, _) => StubHttpHandler.Json("""{"error":"nope"}""", status));
        await Assert.ThrowsAsync<ModelCredentialException>(() => gateway.IssueRuntimeCredentialAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Issue_Unreachable_ThrowsModelCredentialException()
    {
        var (gateway, _) = Create((_, _) => throw new HttpRequestException("connection refused"));
        await Assert.ThrowsAsync<ModelCredentialException>(() => gateway.IssueRuntimeCredentialAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Issue_ResponseWithoutKey_ThrowsModelCredentialException()
    {
        var (gateway, _) = Create((_, _) => StubHttpHandler.Json("{}"));
        await Assert.ThrowsAsync<ModelCredentialException>(() => gateway.IssueRuntimeCredentialAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revoke_DeletesByKeyId()
    {
        var (gateway, handler) = Create((_, _) => StubHttpHandler.Json("""{"deleted_keys":["hashed"]}"""));

        await gateway.RevokeRuntimeCredentialAsync("hashed", TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://litellm:4000/key/delete", request.RequestUri!.ToString());
        Assert.Equal(["hashed"], body!["keys"]!.AsArray().Select(k => k!.GetValue<string>()));
    }

    [Fact]
    public void Credential_ToString_DoesNotLeakTheKey()
    {
        var credential = new RuntimeModelCredential("hashed", "sk-virtual-secret", Now);
        Assert.DoesNotContain("sk-virtual-secret", credential.ToString(), StringComparison.Ordinal);
    }
}
