using System.Net;
using System.Text.Json.Nodes;
using Ymir.VibeMaker.Application.Models;

namespace Ymir.UnitTests.Models;

/// <summary>可手動推進時間的 TimeProvider。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>記錄送出的請求並回傳預先設定的回應。</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, JsonObject? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>計數用的假 gateway：每次發新 key，可模擬撤銷失敗。</summary>
internal sealed class CountingGateway(TimeProvider time) : IModelGateway
{
    public List<RuntimeCredentialRequest> Issued { get; } = [];

    public List<string> Revoked { get; } = [];

    public bool FailRevoke { get; set; }

    public Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken)
    {
        Issued.Add(request);
        var n = Issued.Count;
        return Task.FromResult(new RuntimeModelCredential($"token-{n}", $"sk-key-{n}", time.GetUtcNow() + request.Lifetime));
    }

    public Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken)
    {
        Revoked.Add(keyId);
        return FailRevoke ? Task.FromException(new ModelCredentialException("boom")) : Task.CompletedTask;
    }

    public bool SupportsUsage => false;

    public Task ApplyUserBudgetAsync(Guid userId, decimal? monthlyBudget, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ModelBudgetStatus?> GetBudgetStatusAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<ModelBudgetStatus?>(null);

    public Task<IReadOnlyDictionary<Guid, ModelUserUsage>> GetUsageAsync(IReadOnlyCollection<Guid> userIds, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ModelUserUsage>>(new Dictionary<Guid, ModelUserUsage>());
}
