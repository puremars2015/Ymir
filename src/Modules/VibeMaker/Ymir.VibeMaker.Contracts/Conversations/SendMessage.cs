namespace Ymir.VibeMaker.Contracts.Conversations;

/// <summary><c>POST /api/conversations/{id}/messages</c> 的 request（SA §9.1）。</summary>
/// <param name="ClientRequestId">前端產生的冪等鍵；後端以 (user, clientRequestId) 防止重送。</param>
/// <param name="ModelId">選用的模型（<c>GET /api/models</c> 的 Id）；省略時沿用對話上次的模型或預設模型。</param>
public sealed record SendMessageRequest(string Content, Guid ClientRequestId, string? ModelId = null);

/// <summary><c>202 Accepted</c> 的 response（SA §9.1）。</summary>
public sealed record SendMessageResponse(Guid MessageId, Guid ExecutionId, string EventStreamUrl);
