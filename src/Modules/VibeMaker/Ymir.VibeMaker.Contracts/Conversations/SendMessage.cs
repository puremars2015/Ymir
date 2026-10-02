namespace Ymir.VibeMaker.Contracts.Conversations;

/// <summary><c>POST /api/conversations/{id}/messages</c> 的 request（SA §9.1）。</summary>
/// <param name="ClientRequestId">前端產生的冪等鍵；後端以 (user, clientRequestId) 防止重送。</param>
public sealed record SendMessageRequest(string Content, Guid ClientRequestId);

/// <summary><c>202 Accepted</c> 的 response（SA §9.1）。</summary>
public sealed record SendMessageResponse(Guid MessageId, Guid ExecutionId, string EventStreamUrl);
