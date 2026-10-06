namespace Ymir.VibeMaker.Application.Executions;

/// <summary>已保存、可推送給瀏覽器的事件（SSE 的 id / event / data）。</summary>
public sealed record StoredExecutionEvent(Guid ExecutionId, long Sequence, string EventType, string Data, bool IsTerminal);

/// <summary>把待執行的 execution 交給背景 worker（in-memory 佇列）。Execution 與 HTTP request 解耦，由背景 worker 執行（開發規劃 §5）。</summary>
public interface IExecutionDispatcher
{
    ValueTask EnqueueAsync(Guid executionId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Execution 事件的即時推送。MVP 為 in-memory（單一 API instance）；多 instance 時換成 Redis 等實作，介面不變。
/// 事件一律先寫入資料庫再發佈，訂閱者斷線後可從資料庫補齊（<c>Last-Event-ID</c>）。
/// </summary>
public interface IExecutionEventBus
{
    void Publish(StoredExecutionEvent executionEvent);

    /// <summary>訂閱某個 execution 之後發佈的事件；Dispose 時取消訂閱。</summary>
    ExecutionEventSubscription Subscribe(Guid executionId);
}

public abstract class ExecutionEventSubscription : IDisposable
{
    public abstract IAsyncEnumerable<StoredExecutionEvent> ReadAllAsync(CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}

/// <summary>執行中 execution 的取消開關（使用者按下停止時觸發，SA §5 Stop）。</summary>
public interface IExecutionCancellationRegistry
{
    CancellationTokenSource Register(Guid executionId);

    void Unregister(Guid executionId);

    /// <returns>此 instance 上有正在執行的 execution 並已送出取消時為 true。</returns>
    bool TryCancel(Guid executionId);
}

/// <summary>
/// 部署設定的執行政策預設值（<c>VibeMaker:Runtime:*</c>，SA §17）。管理介面儲存的值優先（<see cref="Runtime.RuntimePolicyService"/>，ADR-0011）。
/// </summary>
public sealed class ExecutionOptions
{
    /// <summary>單次 Agent execution 的逾時（<c>ExecutionTimeoutMinutes</c>）。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>沒有 execution 多久後停止 runtime（<c>IdleTimeoutMinutes</c>）；<see cref="TimeSpan.Zero"/> 表示不自動停止。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>每位使用者同時排隊 + 執行中的 execution 上限（<c>MaxPendingExecutionsPerUser</c>）。</summary>
    public int MaxPendingExecutionsPerUser { get; set; } = 5;

    /// <summary>每位使用者過去 24 小時可建立的 execution 數（<c>DailyExecutionLimit</c>）；0 表示不限制。</summary>
    public int DailyExecutionLimit { get; set; }

    /// <summary>閒置檢查的間隔（<c>IdleCheckIntervalSeconds</c>，測試可調短）。</summary>
    public TimeSpan IdleCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}
