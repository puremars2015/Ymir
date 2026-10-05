using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Infrastructure.PiAgent;

namespace Ymir.VibeMaker.Infrastructure.LiteLlm;

/// <summary>
/// 開發用：沒有 LiteLLM 時直接回傳 <c>VibeMaker:Pi:DevelopmentApiKey</c>（給 Fake LLM）。
/// 只允許 Development 環境（DI 會在其他環境拒絕啟動，ADR-0004）。
/// </summary>
internal sealed class DevelopmentModelGateway(IOptions<PiAgentOptions> options) : IModelGateway
{
    public Task<RuntimeModelCredential> IssueRuntimeCredentialAsync(RuntimeCredentialRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new RuntimeModelCredential("development", options.Value.DevelopmentApiKey ?? "dev-key", DateTimeOffset.MaxValue));

    public Task RevokeRuntimeCredentialAsync(string keyId, CancellationToken cancellationToken) => Task.CompletedTask;
}
