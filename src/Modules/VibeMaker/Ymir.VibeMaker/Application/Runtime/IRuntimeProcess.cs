namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>在 runtime 內執行中的程序，提供 stdin/stdout 串流（Agent harness 以此對接 Pi RPC）。</summary>
public interface IRuntimeProcess : IAsyncDisposable
{
    Stream StandardInput { get; }

    Stream StandardOutput { get; }

    bool HasExited { get; }

    /// <summary>關閉 stdin（送出 EOF），可重複呼叫。Pi 收到 EOF 後會正常結束。</summary>
    void CloseStandardInput();

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>強制結束程序（abort 寬限期過後使用）。</summary>
    void Kill();

    /// <summary>最近的 stderr 內容，僅供 server log 診斷，不可回傳給瀏覽器。</summary>
    string GetStandardErrorTail();
}
