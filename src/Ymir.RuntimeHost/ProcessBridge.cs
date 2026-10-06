using System.Net.WebSockets;
using System.Text.Json;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>
/// 把 runtime 內的程序（<c>podman exec -i</c>）接到 WebSocket（ADR-0008），協定見 <see cref="RuntimeHostProtocol"/>：
/// 第一個 text frame 是啟動訊息；之後 binary frame 為 stdin / stdout，text frame 為控制訊息。
/// API 斷線時一定會結束程序，不留下孤兒程序。
/// </summary>
internal sealed class ProcessBridge(RuntimeRegistry registry, IHostApplicationLifetime lifetime, ILogger<ProcessBridge> logger)
{
    private const int BufferSize = 16 * 1024;
    private static readonly TimeSpan s_startTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_gracePeriod = TimeSpan.FromSeconds(5);

    public async Task RunAsync(WebSocket socket, Guid runtimeId, CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(socket);
        var spec = await ReceiveStartAsync(socket, requestAborted);
        if (spec is null)
        {
            await SendErrorAsync(socket, "Invalid start message.");
            return;
        }

        if (RuntimeHostProtocol.Validate(spec) is { } validationError)
        {
            logger.LogWarning("Rejected process for runtime {RuntimeId}: {Reason}", runtimeId, validationError);
            await SendErrorAsync(socket, validationError);
            return;
        }

        IRuntimeProcess process;
        try
        {
            process = await registry.Manager.StartProcessAsync(runtimeId, spec, requestAborted);
        }
#pragma warning disable CA1031 // 啟動失敗只回摘要給 API，細節留在這裡的 log（SA §12）。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Failed to start {Executable} in runtime {RuntimeId}", spec.Executable, runtimeId);
            await SendErrorAsync(socket, "Failed to start process.");
            return;
        }

        await using (process)
        {
            // 環境變數的值（例如模型金鑰）與參數不寫 log。
            logger.LogInformation("Started {Executable} in runtime {RuntimeId} at {WorkingDirectory}", spec.Executable, runtimeId, spec.WorkingDirectory);
            using var stopping = lifetime.ApplicationStopping.Register(process.Kill);
            await SendControlAsync(socket, new ProcessControlMessage(ProcessControlMessage.Started), requestAborted);

            var pump = PumpOutputAsync(socket, process);
            var receive = ReceiveInputAsync(socket, process);
            var first = await Task.WhenAny(pump, receive);
            if (first == receive && !process.HasExited)
            {
                // API 斷線或提早關閉：先給程序結束的機會，再強制結束。
                logger.LogWarning("Runtime host client disconnected before {Executable} exited; terminating", spec.Executable);
                await TerminateAsync(process);
            }

            await pump;
            if (await Task.WhenAny(receive, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None)) != receive)
            {
                socket.Abort();
            }

            await receive;
        }
    }

    private async Task<RuntimeProcessSpec?> ReceiveStartAsync(WebSocket socket, CancellationToken requestAborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(s_startTimeout);
        var buffer = new byte[BufferSize];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > RuntimeHostProtocol.MaxStartMessageBytes)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return JsonSerializer.Deserialize<ProcessStartMessage>(message.ToArray(), RuntimeHostProtocol.JsonOptions)?.ToSpec();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or OperationCanceledException or WebSocketException)
        {
            logger.LogWarning(ex, "Invalid or missing process start message");
            return null;
        }
    }

    /// <summary>stdout → binary frame；結束後送出 exit（含 stderr 尾端，只供 API 的 server log）並關閉輸出方向。</summary>
    private async Task PumpOutputAsync(WebSocket socket, IRuntimeProcess process)
    {
        var buffer = new byte[BufferSize];
        try
        {
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer, CancellationToken.None)) > 0)
            {
                await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
            }

            var exitCode = await process.WaitForExitAsync(CancellationToken.None);
            await SendControlAsync(socket, new ProcessControlMessage(ProcessControlMessage.Exit, exitCode, process.GetStandardErrorTail()), CancellationToken.None);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Process output stream ended unexpectedly");
            await TerminateAsync(process);
        }
    }

    /// <summary>binary frame → stdin；text frame → closeStdin / kill。回傳時代表對方已關閉或斷線。</summary>
    private async Task ReceiveInputAsync(WebSocket socket, IRuntimeProcess process)
    {
        var buffer = new byte[BufferSize];
        using var text = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await WriteStandardInputAsync(process, buffer.AsMemory(0, result.Count));
                    continue;
                }

                text.Write(buffer, 0, result.Count);
                if (text.Length > RuntimeHostProtocol.MaxStartMessageBytes)
                {
                    return;
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var control = JsonSerializer.Deserialize<ProcessControlMessage>(text.ToArray(), RuntimeHostProtocol.JsonOptions);
                text.SetLength(0);
                switch (control?.Type)
                {
                    case ProcessControlMessage.CloseStdin:
                        process.CloseStandardInput();
                        break;
                    case ProcessControlMessage.Kill:
                        process.Kill();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or JsonException)
        {
            logger.LogDebug(ex, "Process input stream ended");
        }
    }

    private static async Task WriteStandardInputAsync(IRuntimeProcess process, ReadOnlyMemory<byte> data)
    {
        try
        {
            await process.StandardInput.WriteAsync(data, CancellationToken.None);
            await process.StandardInput.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 程序已結束或 stdin 已關閉：與本機管線相同，丟棄。
        }
    }

    private static async Task TerminateAsync(IRuntimeProcess process)
    {
        process.CloseStandardInput();
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(s_gracePeriod);
        }
        catch (TimeoutException)
        {
            process.Kill();
        }
    }

    private async Task SendErrorAsync(WebSocket socket, string message)
    {
        try
        {
            await SendControlAsync(socket, new ProcessControlMessage(ProcessControlMessage.Error, Message: message), CancellationToken.None);
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Could not deliver process error to client");
        }
    }

    private static Task SendControlAsync(WebSocket socket, ProcessControlMessage message, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, RuntimeHostProtocol.JsonOptions), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
}
