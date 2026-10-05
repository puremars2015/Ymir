using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// 以 Pi RPC 模式執行 Agent：每次 run 在 runtime 內啟動一個 <c>pi --mode rpc</c> 程序，
/// 透過 stdin/stdout JSONL 對接，以 <c>--session-id</c> 續接同一個 Agent session（ADR-0003）。
/// </summary>
internal sealed class PiAgentHarness(
    IAgentRuntimeManager runtimeManager,
    IOptions<PiAgentOptions> options,
    ModelCatalog models,
    ILogger<PiAgentHarness> logger) : IAgentHarness
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PiAgentOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, string> _provisionedConfigHashes = new();

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var pump = PumpAsync(request, channel.Writer, cancellationToken);

        // Pump 一定會寫入終止事件並關閉 channel，所以這裡不使用 cancellationToken，避免漏掉 cancelled 事件。
        await foreach (var agentEvent in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            yield return agentEvent;
        }

        await pump.ConfigureAwait(false);
    }

    /// <summary>附加 system prompt 的檔案（runtime 內路徑）；以檔案傳給 Pi，內容不會出現在 host 的程序參數。</summary>
    internal static string SystemPromptPath(Guid executionId, int index) => $"{PiRuntimeLayout.PromptDirectory}/{executionId:N}-{index}.md";

    internal RuntimeProcessSpec BuildProcessSpec(AgentRunRequest request) =>
        new(
            _options.Executable,
            [
                "--mode", "rpc",
                "--provider", _options.ProviderName,
                "--model", request.ModelId,
                "--session-dir", PiRuntimeLayout.SessionDirectory,
                "--session-id", request.SessionId.ToString("D"),
                .. request.SystemPrompts.SelectMany((_, i) => new[] { "--append-system-prompt", SystemPromptPath(request.ExecutionId, i) }),
            ],
            new Dictionary<string, string>
            {
                ["PI_CODING_AGENT_DIR"] = PiRuntimeLayout.AgentDirectory,
                ["PI_OFFLINE"] = "1",
                ["PI_SKIP_VERSION_CHECK"] = "1",
                ["PI_TELEMETRY"] = "0",
                [PiRuntimeLayout.ApiKeyEnvironmentVariable] = request.ModelApiKey,
            },
            // 專案目錄或未分組對話自己的目錄（ADR-0007）；Pi 的檔案工具以此為根。
            request.WorkingDirectory);

    private async Task PumpAsync(AgentRunRequest request, ChannelWriter<AgentEvent> writer, CancellationToken cancellationToken)
    {
        var mapper = new PiRpcEventMapper();
        IRuntimeProcess? process = null;
        using var stdinLock = new SemaphoreSlim(1, 1);
        using var readCts = new CancellationTokenSource();

        try
        {
            await EnsureConfigProvisionedAsync(request.RuntimeId, cancellationToken).ConfigureAwait(false);
            await WriteSystemPromptsAsync(request, cancellationToken).ConfigureAwait(false);

            // 使用者的 LiteLLM virtual key（ADR-0004），由 ExecutionRunner 經 IModelGateway 取得；只以環境變數名稱傳入 runtime。
            process = await runtimeManager.StartProcessAsync(request.RuntimeId, BuildProcessSpec(request), cancellationToken)
                .ConfigureAwait(false);
            var stdin = process.StandardInput;

            if (!_options.AutoRetry)
            {
                await SendCommandAsync(stdin, stdinLock, new { id = "config-auto-retry", type = "set_auto_retry", enabled = false }, cancellationToken)
                    .ConfigureAwait(false);
            }

            var promptId = $"prompt-{request.ExecutionId:N}";
            await SendCommandAsync(stdin, stdinLock, new { id = promptId, type = "prompt", message = request.Prompt }, cancellationToken)
                .ConfigureAwait(false);

            using var abortRegistration = cancellationToken.Register(() => _ = AbortAsync(stdin, stdinLock, readCts, request.ExecutionId));

            await foreach (var line in JsonlReader.ReadRecordsAsync(process.StandardOutput, readCts.Token).ConfigureAwait(false))
            {
                if (!TryParse(line, out var record))
                {
                    continue;
                }

                if (TryHandleResponse(record, promptId, mapper, out var responseEvent))
                {
                    if (responseEvent is not null)
                    {
                        await writer.WriteAsync(responseEvent, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    continue;
                }

                foreach (var agentEvent in mapper.Map(record))
                {
                    await writer.WriteAsync(agentEvent, CancellationToken.None).ConfigureAwait(false);
                }

                if (mapper.IsTerminal)
                {
                    if (mapper.LastErrorDetail is { } detail)
                    {
                        logger.LogWarning("Pi execution {ExecutionId} ended with model error: {Detail}", request.ExecutionId, detail);
                    }

                    return;
                }
            }

            // stdout 結束卻沒有收到 agent_settled。
            if (cancellationToken.IsCancellationRequested)
            {
                await writer.WriteAsync(new AgentCancelled(mapper.AccumulatedText), CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                var stderr = process.GetStandardErrorTail();
                logger.LogError("Pi process for execution {ExecutionId} exited unexpectedly. stderr: {Stderr}", request.ExecutionId, stderr);
                await writer.WriteAsync(mapper.CreateUnexpectedExitEvent(), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 尚未啟動程序就被取消，或 abort 寬限期已過。
            process?.Kill();
            await writer.WriteAsync(new AgentCancelled(mapper.AccumulatedText), CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 任何失敗都必須轉成 execution.failed 事件，不能讓 execution 卡住（SA §14）。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var stderr = process?.GetStandardErrorTail();
            logger.LogError(ex, "Pi execution {ExecutionId} failed. stderr: {Stderr}", request.ExecutionId, stderr);
            await writer.WriteAsync(
                new AgentFailed(ExecutionErrorCodes.AgentRuntimeError, "Agent 執行環境發生錯誤。"),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (process is not null)
            {
                await process.DisposeAsync().ConfigureAwait(false);
            }

            await DeleteSystemPromptsAsync(request).ConfigureAwait(false);
            writer.TryComplete();
        }
    }

    /// <summary>把這次的 system prompt 寫成 runtime 內的檔案（與 models.json 相同，經 stdin 寫入，不經程序參數）。</summary>
    private async Task WriteSystemPromptsAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.SystemPrompts.Count; i++)
        {
            var spec = new RuntimeProcessSpec(
                "sh",
                ["-c", "mkdir -p \"$1\" && cat > \"$2\"", "ymir-prompt", PiRuntimeLayout.PromptDirectory, SystemPromptPath(request.ExecutionId, i)]);
            var writerProcess = await runtimeManager.StartProcessAsync(request.RuntimeId, spec, cancellationToken).ConfigureAwait(false);
            await using (writerProcess.ConfigureAwait(false))
            {
                await writerProcess.StandardInput.WriteAsync(Encoding.UTF8.GetBytes(request.SystemPrompts[i]), cancellationToken).ConfigureAwait(false);
                writerProcess.CloseStandardInput();
                var exitCode = await writerProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"Writing system prompt failed with exit code {exitCode}: {writerProcess.GetStandardErrorTail()}");
                }
            }
        }
    }

    private async Task DeleteSystemPromptsAsync(AgentRunRequest request)
    {
        if (request.SystemPrompts.Count == 0)
        {
            return;
        }

        try
        {
            var paths = Enumerable.Range(0, request.SystemPrompts.Count).Select(i => SystemPromptPath(request.ExecutionId, i));
            var cleanup = await runtimeManager.StartProcessAsync(request.RuntimeId, new RuntimeProcessSpec("rm", ["-f", .. paths]), CancellationToken.None)
                .ConfigureAwait(false);
            await using (cleanup.ConfigureAwait(false))
            {
                cleanup.CloseStandardInput();
                await cleanup.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // 清理失敗不影響結果（檔案在使用者自己的 runtime 內），只記錄警告。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to delete system prompt files for execution {ExecutionId}", request.ExecutionId);
        }
    }

    private async Task AbortAsync(Stream stdin, SemaphoreSlim stdinLock, CancellationTokenSource readCts, Guid executionId)
    {
        try
        {
            logger.LogInformation("Aborting Pi execution {ExecutionId}", executionId);
            await SendCommandAsync(stdin, stdinLock, new { id = "abort", type = "abort" }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 程序可能已經結束。
        }
        finally
        {
            // 寬限期內 Pi 應送出 stopReason=aborted 與 agent_settled；否則中斷讀取並強制結束程序。
            try
            {
                readCts.CancelAfter(_options.AbortGracePeriod);
            }
            catch (ObjectDisposedException)
            {
                // run 已結束。
            }
        }
    }

    private async Task EnsureConfigProvisionedAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var modelsJson = PiModelsConfig.Build(_options, models.Models.Select(m => m.Id));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modelsJson)));
        if (_provisionedConfigHashes.TryGetValue(runtimeId, out var existing) && existing == hash)
        {
            return;
        }

        // 透過 runtime 內的 shell 寫入設定，與 runtime provider（Local / Podman）無關。
        var spec = new RuntimeProcessSpec(
            "sh",
            ["-c", "mkdir -p \"$1\" \"$2\" && cat > \"$1/models.json\"", "ymir-provision", PiRuntimeLayout.AgentDirectory, PiRuntimeLayout.SessionDirectory]);
        var provision = await runtimeManager.StartProcessAsync(runtimeId, spec, cancellationToken).ConfigureAwait(false);
        await using (provision.ConfigureAwait(false))
        {
            await provision.StandardInput.WriteAsync(Encoding.UTF8.GetBytes(modelsJson), cancellationToken).ConfigureAwait(false);
            provision.CloseStandardInput();
            var exitCode = await provision.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Provisioning Pi config failed with exit code {exitCode}: {provision.GetStandardErrorTail()}");
            }
        }

        _provisionedConfigHashes[runtimeId] = hash;
    }

    private static async Task SendCommandAsync(Stream stdin, SemaphoreSlim stdinLock, object command, CancellationToken cancellationToken)
    {
        // System.Text.Json 預設會把非 ASCII（含 U+2028/U+2029）轉義，符合 Pi 的 LF-only framing。
        var payload = JsonSerializer.SerializeToUtf8Bytes(command, s_jsonOptions);
        await stdinLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stdin.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            stdin.WriteByte((byte)'\n');
            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stdinLock.Release();
        }
    }

    private bool TryParse(string line, out JsonElement record)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            record = document.RootElement.Clone();
            return record.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Ignoring non-JSON record from Pi stdout");
            record = default;
            return false;
        }
    }

    /// <summary>處理 command response。回傳 true 表示此 record 是 response；<paramref name="terminal"/> 非 null 時 run 結束。</summary>
    private bool TryHandleResponse(JsonElement record, string promptId, PiRpcEventMapper mapper, out AgentEvent? terminal)
    {
        terminal = null;
        if (!record.TryGetProperty("type", out var type) || type.GetString() != "response")
        {
            return false;
        }

        var id = record.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var success = record.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
        if (!success)
        {
            var error = record.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            logger.LogWarning("Pi command {CommandId} failed: {Error}", id, error);
            if (id == promptId)
            {
                terminal = new AgentFailed(ExecutionErrorCodes.AgentRuntimeError, "Agent 無法處理此請求。");
            }

            return true;
        }

        if (id == promptId
            && record.TryGetProperty("data", out var data)
            && data.TryGetProperty("disposition", out var disposition)
            && disposition.GetString() == "handled")
        {
            // 沒有啟動 agent run（例如 extension 指令），不會有 agent_settled。
            terminal = new AgentCompleted(mapper.AccumulatedText);
        }

        return true;
    }
}
