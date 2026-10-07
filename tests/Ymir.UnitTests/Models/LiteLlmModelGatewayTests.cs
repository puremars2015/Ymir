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

        // 先 upsert LiteLLM 使用者，再發掛在該使用者底下的 key（ADR-0011）
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("http://litellm:4000/user/update", handler.Requests[0].Request.RequestUri!.ToString());
        var (request, body) = handler.Requests[1];
        Assert.Equal("http://litellm:4000/key/generate", request.RequestUri!.ToString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", body!["user_id"]!.GetValue<string>());
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

        Assert.False(handler.Requests[^1].Body!.ContainsKey("max_budget"));
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

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ApplyBudget_UpdatesExistingUser_WithThirtyDayBudget()
    {
        var (gateway, handler) = Create((_, _) => StubHttpHandler.Json("""{"user_id":"x"}"""));

        await gateway.ApplyUserBudgetAsync(UserId, 25m, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://litellm:4000/user/update", request.RequestUri!.ToString());
        Assert.Equal(MasterKey, request.Headers.Authorization!.Parameter);
        Assert.Equal(UserId.ToString("D"), body!["user_id"]!.GetValue<string>());
        Assert.Equal(25m, body["max_budget"]!.GetValue<decimal>());
        Assert.Equal("30d", body["budget_duration"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyBudget_CreatesTheUser_WhenUpdateSaysItDoesNotExist()
    {
        var (gateway, handler) = Create((request, _) => request.RequestUri!.AbsolutePath == "/user/update"
            ? StubHttpHandler.Json("""{"error":"user not found"}""", HttpStatusCode.BadRequest)
            : StubHttpHandler.Json("""{"user_id":"x"}"""));

        await gateway.ApplyUserBudgetAsync(UserId, null, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        var (request, body) = handler.Requests[1];
        Assert.Equal("http://litellm:4000/user/new", request.RequestUri!.ToString());
        Assert.Null(body!["max_budget"]); // 不限制
        Assert.Null(body["budget_duration"]);
        Assert.False(body["auto_create_key"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ApplyBudget_DoesNotTryToCreate_OnAuthOrServerErrors(HttpStatusCode status)
    {
        var (gateway, handler) = Create((_, _) => StubHttpHandler.Json("{}", status));

        await Assert.ThrowsAsync<ModelCredentialException>(() => gateway.ApplyUserBudgetAsync(UserId, 10m, TestContext.Current.CancellationToken));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BudgetStatus_ParsesUserInfo_AndIsNullForUnknownUsers()
    {
        var (gateway, _) = Create((request, _) => request.RequestUri!.Query.Contains(UserId.ToString("D"), StringComparison.Ordinal)
            ? StubHttpHandler.Json("""{"user_id":"x","user_info":{"spend":12.5,"max_budget":10,"budget_reset_at":"2026-11-01T00:00:00Z"},"keys":[]}""")
            : StubHttpHandler.Json("{}", HttpStatusCode.NotFound));

        var status = await gateway.GetBudgetStatusAsync(UserId, TestContext.Current.CancellationToken);
        var unknown = await gateway.GetBudgetStatusAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(new ModelBudgetStatus(12.5m, 10m, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), status);
        Assert.True(status!.IsExceeded);
        Assert.Null(unknown);
    }

    [Fact]
    public void ParseBudget_WithoutLimit_IsNeverExceeded()
    {
        var status = LiteLlmModelGateway.ParseBudget(System.Text.Json.Nodes.JsonNode.Parse("""{"user_info":{"spend":3,"max_budget":null}}""")!.AsObject());
        Assert.Null(status.MaxBudget);
        Assert.False(status.IsExceeded);
    }

    [Fact]
    public async Task Usage_SumsDailyActivityAcrossPages_AndSkipsUsersThatFail()
    {
        var failing = Guid.NewGuid();
        var (gateway, handler) = Create((request, _) =>
        {
            var uri = request.RequestUri!;
            if (uri.Query.Contains(failing.ToString("D"), StringComparison.Ordinal))
            {
                return StubHttpHandler.Json("{}", HttpStatusCode.InternalServerError);
            }

            if (uri.AbsolutePath == "/user/info")
            {
                return StubHttpHandler.Json("""{"user_info":{"spend":0.5,"max_budget":20}}""");
            }

            return uri.Query.Contains("page=1&", StringComparison.Ordinal)
                ? StubHttpHandler.Json("""{"results":[{"date":"2026-10-04","metrics":{"spend":0.25,"prompt_tokens":100,"completion_tokens":40,"api_requests":2}}],"metadata":{"has_more":true}}""")
                : StubHttpHandler.Json("""{"results":[{"date":"2026-10-05","metrics":{"spend":0.5,"prompt_tokens":200,"completion_tokens":60,"api_requests":3}}],"metadata":{"has_more":false}}""");
        });

        var usage = await gateway.GetUsageAsync([UserId, failing], new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 5), TestContext.Current.CancellationToken);

        var row = Assert.Single(usage).Value;
        Assert.Equal(new ModelUserUsage(0.75m, 300, 100, 5, new ModelBudgetStatus(0.5m, 20m, null)), row);
        Assert.Contains(handler.Requests, r => r.Request.RequestUri!.Query.Contains("start_date=2026-09-29&end_date=2026-10-05", StringComparison.Ordinal));
    }

    [Fact]
    public void Credential_ToString_DoesNotLeakTheKey()
    {
        var credential = new RuntimeModelCredential("hashed", "sk-virtual-secret", Now);
        Assert.DoesNotContain("sk-virtual-secret", credential.ToString(), StringComparison.Ordinal);
    }
}
