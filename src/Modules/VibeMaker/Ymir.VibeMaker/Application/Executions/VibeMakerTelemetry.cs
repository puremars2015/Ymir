using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ymir.VibeMaker.Application.Executions;

/// <summary>
/// SA §18 的監控指標與 execution 追蹤。Meter / ActivitySource 名稱以 <c>Ymir.</c> 開頭，
/// 由 ServiceDefaults 的 OpenTelemetry 設定（<c>AddMeter("Ymir.*")</c>、<c>AddSource("Ymir.*")</c>）匯出。
/// <list type="bullet">
/// <item><c>ymir.runtimes.active</c>：執行中的 runtime 數（資料庫紀錄，背景每輪更新）</item>
/// <item><c>ymir.runtimes.busy</c>：正在執行 Agent 的 runtime 數</item>
/// <item><c>ymir.executions.started</c> / <c>ymir.executions.finished</c>（status、error_code）</item>
/// <item><c>ymir.execution.duration</c>：Agent 執行時間（秒，從開始到結束）</item>
/// <item><c>ymir.runtime.start_failures</c>：EnsureRuntime 失敗次數</item>
/// </list>
/// </summary>
public sealed class VibeMakerTelemetry : IDisposable
{
    public const string Name = "Ymir.VibeMaker";

    private readonly Meter _meter;
    private readonly Counter<long> _started;
    private readonly Counter<long> _finished;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _runtimeStartFailures;
    private long _busy;
    private long _activeRuntimes;

    public VibeMakerTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(Name);
        _started = _meter.CreateCounter<long>("ymir.executions.started", description: "Agent executions that started running.");
        _finished = _meter.CreateCounter<long>("ymir.executions.finished", description: "Agent executions that reached a terminal state.");
        _duration = _meter.CreateHistogram<double>("ymir.execution.duration", unit: "s", description: "Agent execution duration from start to end.");
        _runtimeStartFailures = _meter.CreateCounter<long>("ymir.runtime.start_failures", description: "Failures to create or start a user runtime.");
        _meter.CreateObservableGauge("ymir.runtimes.busy", () => Interlocked.Read(ref _busy), description: "Runtimes currently running an agent execution.");
        _meter.CreateObservableGauge("ymir.runtimes.active", () => Interlocked.Read(ref _activeRuntimes), description: "Runtimes recorded as created or running.");
    }

    /// <summary>每個 execution 一個 activity：log 與稽核的 trace id 因此能串起同一次執行（AC-10）。</summary>
    public static ActivitySource ActivitySource { get; } = new(Name);

    public long BusyRuntimes => Interlocked.Read(ref _busy);

    public void ExecutionStarted()
    {
        _started.Add(1);
        Interlocked.Increment(ref _busy);
    }

    /// <param name="started">execution 是否曾經開始（runtime 準備失敗時沒有開始，不算 busy）。</param>
    public void ExecutionFinished(bool started, string status, string? errorCode, TimeSpan? duration)
    {
        if (started)
        {
            Interlocked.Decrement(ref _busy);
        }

        var tags = new TagList { { "status", status }, { "error_code", errorCode ?? string.Empty } };
        _finished.Add(1, tags);
        if (duration is { } value)
        {
            _duration.Record(value.TotalSeconds, new TagList { { "status", status } });
        }
    }

    public void RuntimeStartFailed() => _runtimeStartFailures.Add(1);

    public void SetActiveRuntimes(long count) => Interlocked.Exchange(ref _activeRuntimes, count);

    public void Dispose() => _meter.Dispose();
}
