using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Runtime.Remote;

/// <summary>
/// Runtime host 上執行中的程序（ADR-0008）：stdin / stdout 經 WebSocket 轉送，行為與 <c>HostProcess</c> 相同，
/// 讓 <c>PiAgentHarness</c> 不需要知道 runtime 在本機或遠端。
/// <list type="bullet">
/// <item>Client → server：binary frame 為 stdin；text frame 為 <c>closeStdin</c> / <c>kill</c>。</item>
/// <item>Server → client：binary frame 為 stdout；text frame 為 <c>exit</c>（含 exit code 與 stderr 尾端，只供 server log）。</item>
/// </list>
/// 所有送出的 frame 經由同一個 queue 依序送出（WebSocket 同時只能有一個 SendAsync），所以同步的 <see cref="Kill"/>、
/// <see cref="CloseStandardInput"/> 與 stdin 寫入不會打亂順序。
/// </summary>
internal sealed class RemoteRuntimeProcess : IRuntimeProcess
{
    private const int ReceiveBufferSize = 16 * 1024;
    private const string ConnectionLostMessage = "Runtime host connection lost.";

    private readonly WebSocket _socket;
    private readonly ILogger _logger;
    private readonly Pipe _stdout = new(new PipeOptions(pauseWriterThreshold: 1024 * 1024, resumeWriterThreshold: 512 * 1024, useSynchronizationContext: false));
    private readonly Channel<OutgoingFrame> _outgoing = Channel.CreateUnbounded<OutgoingFrame>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stream _standardOutput;
    private readonly RemoteStandardInput _standardInput;
    private Task _sender = Task.CompletedTask;
    private Task _receiver = Task.CompletedTask;
    private volatile string _stderrTail = string.Empty;
    private int _stdinClosed;

    private RemoteRuntimeProcess(WebSocket socket, ILogger logger)
    {
        _socket = socket;
        _logger = logger;
        _standardOutput = _stdout.Reader.AsStream();
        _standardInput = new RemoteStandardInput(this);
    }

    public Stream StandardInput => _standardInput;

    public Stream StandardOutput => _standardOutput;

    public bool HasExited => _exit.Task.IsCompleted;

    /// <summary>送出啟動訊息並等待 runtime host 回覆 <c>started</c>；被拒絕時拋出例外（與本機啟動失敗相同）。</summary>
    public static async Task<RemoteRuntimeProcess> StartAsync(WebSocket socket, RuntimeProcessSpec spec, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        var start = JsonSerializer.SerializeToUtf8Bytes(ProcessStartMessage.From(spec), RuntimeHostProtocol.JsonOptions);
        await socket.SendAsync(start, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);

        var reply = await ReceiveControlAsync(socket, cancellationToken).ConfigureAwait(false);
        if (reply?.Type != ProcessControlMessage.Started)
        {
            throw new InvalidOperationException($"Runtime host refused to start the process: {reply?.Message ?? "no response"}");
        }

        var process = new RemoteRuntimeProcess(socket, logger);
        process._sender = process.SendLoopAsync();
        process._receiver = process.ReceiveLoopAsync();
        return process;
    }

    public void CloseStandardInput()
    {
        if (Interlocked.Exchange(ref _stdinClosed, 1) == 0)
        {
            Enqueue(OutgoingFrame.Control(ProcessControlMessage.CloseStdin));
        }
    }

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

    public void Kill()
    {
        if (!HasExited)
        {
            Enqueue(OutgoingFrame.Control(ProcessControlMessage.Kill));
        }
    }

    public string GetStandardErrorTail() => _stderrTail;

    public async ValueTask DisposeAsync()
    {
        CloseStandardInput();
        if (!await WaitAsync(_exit.Task, TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            Kill();
            await WaitAsync(_exit.Task, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        // 呼叫端不再讀 stdout：結束 reader，讓接收迴圈不會卡在 backpressure。
        await _stdout.Reader.CompleteAsync().ConfigureAwait(false);
        if (!await WaitAsync(_receiver, TimeSpan.FromSeconds(5)).ConfigureAwait(false))
        {
            _socket.Abort();
            await _receiver.ConfigureAwait(false);
        }

        _outgoing.Writer.TryComplete();
        await _sender.ConfigureAwait(false);
        _socket.Dispose();
    }

    internal void EnqueueStandardInput(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if (Volatile.Read(ref _stdinClosed) == 1)
        {
            throw new ObjectDisposedException(nameof(StandardInput), "Standard input has been closed.");
        }

        if (!_outgoing.Writer.TryWrite(new OutgoingFrame(data.ToArray(), WebSocketMessageType.Binary, IsClose: false)))
        {
            throw new IOException("The runtime process is no longer running.");
        }
    }

    private void Enqueue(OutgoingFrame frame) => _outgoing.Writer.TryWrite(frame);

    private async Task SendLoopAsync()
    {
        await foreach (var frame in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (frame.IsClose)
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                }
                else if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _socket.SendAsync(frame.Data, frame.Type, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                // 連線已中斷；接收迴圈會把程序標記為結束。
            }
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[ReceiveBufferSize];
        using var text = new MemoryStream();
        int? exitCode = null;
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Enqueue(OutgoingFrame.CloseFrame);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Reader 已結束（呼叫端不再讀）時直接丟棄。
                    await _stdout.Writer.WriteAsync(buffer.AsMemory(0, result.Count)).ConfigureAwait(false);
                    continue;
                }

                text.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var control = JsonSerializer.Deserialize<ProcessControlMessage>(text.ToArray(), RuntimeHostProtocol.JsonOptions);
                text.SetLength(0);
                if (control?.Type == ProcessControlMessage.Exit)
                {
                    _stderrTail = control.StderrTail ?? string.Empty;
                    exitCode = control.ExitCode ?? -1;
                    // stdout 已在 exit 之前全部送達。
                    await _stdout.Writer.CompleteAsync().ConfigureAwait(false);
                    _exit.TrySetResult(exitCode.Value);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Runtime host process stream ended unexpectedly");
        }
        finally
        {
            if (exitCode is null)
            {
                _stderrTail = ConnectionLostMessage;
            }

            await _stdout.Writer.CompleteAsync().ConfigureAwait(false);
            _exit.TrySetResult(exitCode ?? -1);
        }
    }

    private static async Task<ProcessControlMessage?> ReceiveControlAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var text = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text)
            {
                return null;
            }

            text.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return JsonSerializer.Deserialize<ProcessControlMessage>(text.ToArray(), RuntimeHostProtocol.JsonOptions);
            }
        }
    }

    private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private sealed record OutgoingFrame(byte[] Data, WebSocketMessageType Type, bool IsClose)
    {
        public static readonly OutgoingFrame CloseFrame = new([], WebSocketMessageType.Close, IsClose: true);

        public static OutgoingFrame Control(string type) =>
            new(JsonSerializer.SerializeToUtf8Bytes(new ProcessControlMessage(type), RuntimeHostProtocol.JsonOptions), WebSocketMessageType.Text, IsClose: false);
    }

    /// <summary>寫入即排入送出 queue（保持順序）；<see cref="Flush"/> 不需要做事。</summary>
    private sealed class RemoteStandardInput(RemoteRuntimeProcess owner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => owner.EnqueueStandardInput(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => owner.EnqueueStandardInput(buffer);

        public override void WriteByte(byte value) => owner.EnqueueStandardInput([value]);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.EnqueueStandardInput(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.EnqueueStandardInput(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }
}
