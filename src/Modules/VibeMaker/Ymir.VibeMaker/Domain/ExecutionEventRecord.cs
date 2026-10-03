namespace Ymir.VibeMaker.Domain;

/// <summary>
/// 已送出的 SSE 事件（開發規劃 §5、§9）。保存下來讓瀏覽器斷線後以 <c>Last-Event-ID</c> 續傳，
/// <see cref="Data"/> 是已經過摘要處理、可以給瀏覽器看的 JSON（SA §10 安全規則）。
/// </summary>
public sealed class ExecutionEventRecord
{
    private ExecutionEventRecord()
    {
    }

    public Guid ExecutionId { get; private set; }

    /// <summary>從 1 開始遞增，即 SSE 的 <c>id:</c>。</summary>
    public long Sequence { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Data { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static ExecutionEventRecord Create(Guid executionId, long sequence, string eventType, string data, DateTimeOffset now) =>
        new() { ExecutionId = executionId, Sequence = sequence, EventType = eventType, Data = data, CreatedAt = now };
}
