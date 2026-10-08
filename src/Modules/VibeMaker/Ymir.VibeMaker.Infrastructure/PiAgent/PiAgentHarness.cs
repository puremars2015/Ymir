using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Agents;
using Ymir.VibeMaker.Application.Attachments;
using Ymir.VibeMaker.Application.Extensions;
using Ymir.VibeMaker.Application.Models;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Contracts.Executions;
using Ymir.VibeMaker.Infrastructure.Files;

namespace Ymir.VibeMaker.Infrastructure.PiAgent;

/// <summary>
/// 以 Pi RPC 模式執行 Agent：每次 run 在 runtime 內啟動一個 <c>pi --mode rpc</c> 程序，
/// 透過 stdin/stdout JSONL 對接，以 <c>--session-id</c> 續接同一個 Agent session（ADR-0003）。
/// </summary>
internal sealed class PiAgentHarness(
    IAgentRuntimeManager runtimeManager,
    IOptions<PiAgentOptions> options,
    ModelCatalog models,
    RuntimeWorkspaceFileReader files,
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
                // 每次明確重設，避免沿用 Pi session 上一次的深度；off 不傳 reasoning_effort，讓供應商採預設。
                "--thinking", request.ThinkingLevel is null or "none" ? "off" : request.ThinkingLevel,
                "--session-dir", PiRuntimeLayout.SessionDirectory,
                "--session-id", request.SessionId.ToString("D"),
                .. request.SystemPrompts.SelectMany((_, i) => new[] { "--append-system-prompt", SystemPromptPath(request.ExecutionId, i) }),
                // 擴充能力由政策決定（ADR-0012 A.3），Agent 無法改變參數。
                .. PiExtensionConfig.BuildArguments(request.Extensions ?? EffectiveExtensions.None, request.PlatformMcp is not null),
            ],
            BuildEnvironment(request),
            // 專案目錄或未分組對話自己的目錄（ADR-0007）；Pi 的檔案工具以此為根。
            request.WorkingDirectory);

    private static Dictionary<string, string> BuildEnvironment(AgentRunRequest request)
    {
        var environment = new Dictionary<string, string>
        {
            ["PI_CODING_AGENT_DIR"] = PiRuntimeLayout.AgentDirectory,
            ["PI_OFFLINE"] = "1",
            ["PI_SKIP_VERSION_CHECK"] = "1",
            ["PI_TELEMETRY"] = "0",
            [PiRuntimeLayout.ApiKeyEnvironmentVariable] = request.ModelApiKey,
        };
        if (request.PlatformMcp is { } platform)
        {
            // 每人專屬的短期 gateway token（ADR-0012 B.3）：與 LiteLLM key 一樣只以環境變數名稱傳入 runtime。
            environment[PiExtensionConfig.PlatformMcpTokenEnvironmentVariable] = platform.Token;
        }

        return environment;
    }

    private async Task PumpAsync(AgentRunRequest request, ChannelWriter<AgentEvent> writer, CancellationToken cancellationToken)
    {
        var mapper = new PiRpcEventMapper();
        IRuntimeProcess? process = null;
        using var stdinLock = new SemaphoreSlim(1, 1);
        using var readCts = new CancellationTokenSource();

        try
        {
            await EnsureConfigProvisionedAsync(request, cancellationToken).ConfigureAwait(false);
            await WriteExtensionConfigAsync(request, cancellationToken).ConfigureAwait(false);
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
            var images = await ReadInlineImagesAsync(request, cancellationToken).ConfigureAwait(false);
            object prompt = images.Count == 0
                ? new { id = promptId, type = "prompt", message = request.Prompt }
                : new { id = promptId, type = "prompt", message = request.Prompt, images };
            await SendCommandAsync(stdin, stdinLock, prompt, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// 支援視覺的模型：把使用者附加的圖片（以檔頭判斷為 PNG / JPEG / GIF / WebP、在大小與張數上限內）以 Pi 的 ImageContent 一併送出。
    /// 其他附件（影片、文件、太大的圖片）只在 prompt 中列出路徑，由 Agent 用工具處理。
    /// </summary>
    private async Task<IReadOnlyList<PiImageContent>> ReadInlineImagesAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        if (!models.SupportsImages(request.ModelId))
        {
            return [];
        }

        var candidates = request.Attachments
            .Where(a => AttachmentRules.IsInlineImage(a.ContentType) && a.Size <= AttachmentRules.MaxInlineImageBytes)
            .Take(AttachmentRules.MaxInlineImages)
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var images = new List<PiImageContent>(candidates.Count);
        await files.ReadInRuntimeAsync(request.RuntimeId, request.WorkingDirectory, [.. candidates.Select(c => c.Path)], async (file, ct) =>
        {
            if (file.Size > AttachmentRules.MaxInlineImageBytes)
            {
                return; // 上傳後被 Agent 換成更大的檔案
            }

            using var buffer = new MemoryStream((int)file.Size);
            await file.Content.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var contentType = candidates.First(c => c.Path == file.Path).ContentType;
            images.Add(new PiImageContent("image", Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length), contentType));
        }, cancellationToken).ConfigureAwait(false);
        return images;
    }

    /// <summary>Pi RPC 的 ImageContent（<c>{"type":"image","data":base64,"mimeType":...}</c>）。</summary>
    internal sealed record PiImageContent(string Type, string Data, string MimeType);

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

    /// <summary>
    /// 每次執行前重寫 Pi 的 <c>settings.json</c>、<c>trust.json</c>、<c>mcp.json</c> 與平台 skill（ADR-0012 A.3、B.3）。
    /// Agent 在上一次執行改寫這些檔也不會延續；Pi 只在 session 啟動時讀取，執行中被改寫不影響這一次。
    /// </summary>
    private async Task WriteExtensionConfigAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var extensions = request.Extensions ?? EffectiveExtensions.None;
        var userMcp = await ReadUserMcpConfigAsync(request.RuntimeId, cancellationToken).ConfigureAwait(false);
        var mcpJson = PiExtensionConfig.BuildMcpConfig(userMcp, extensions.Mcp, out var invalid, request.PlatformMcp?.Servers);
        if (invalid)
        {
            logger.LogWarning("Ignoring invalid {File} for execution {ExecutionId}", PiRuntimeLayout.UserMcpFileName, request.ExecutionId);
        }

        List<(string Path, string Content)> files =
        [
            (PiRuntimeLayout.SettingsPath, PiExtensionConfig.SettingsJson),
            (PiRuntimeLayout.TrustPath, PiExtensionConfig.TrustJson),
            (PiRuntimeLayout.McpConfigPath, mcpJson),
        ];
        if (extensions.Skills || extensions.Mcp)
        {
            files.Add(($"{PiRuntimeLayout.ExtensionBuilderSkillDirectory}/SKILL.md", PiExtensionConfig.BuildExtensionBuilderSkill(extensions)));
        }

        // 以 runtime 內的 node（Pi 本身需要）一次寫入多個檔案。路徑只來自上面的常數、以參數傳入（Local runtime 會換成 host 路徑）；
        // 內容經 stdin，不經程序參數（使用者的 MCP 設定可能含有憑證）。先寫暫存檔再 rename，Agent 預先放的 symlink 會被取代而不是被跟隨。
        var spec = new RuntimeProcessSpec("node", ["-e", WriteFilesScript, .. files.Select(f => f.Path)]);
        var writer = await runtimeManager.StartProcessAsync(request.RuntimeId, spec, cancellationToken).ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            await writer.StandardInput.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(files.Select(f => f.Content).ToList(), s_jsonOptions), cancellationToken)
                .ConfigureAwait(false);
            writer.CloseStandardInput();
            var exitCode = await writer.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Writing Pi extension config failed with exit code {exitCode}: {writer.GetStandardErrorTail()}");
            }
        }
    }

    private const string WriteFilesScript =
        "const fs=require('fs'),path=require('path');let d='';process.stdin.setEncoding('utf8');" +
        "process.stdin.on('data',c=>d+=c).on('end',()=>{const c=JSON.parse(d),p=process.argv.slice(1);" +
        "if(c.length!==p.length)process.exit(2);p.forEach((f,i)=>{fs.mkdirSync(path.dirname(f),{recursive:true});" +
        "const t=f+'.ymir-tmp';fs.writeFileSync(t,c[i]);fs.renameSync(t,f);});});";

    /// <summary>
    /// 讀取使用者自建的 MCP 設定。第一次執行時，把 Ymir 管理之前 Agent 寫的 <c>mcp.json</c> 複製成 <c>mcp.user.json</c>，
    /// 之後 <c>mcp.json</c> 由 Ymir 產生（標記檔避免把產生的檔案又複製回去）。
    /// </summary>
    private async Task<string?> ReadUserMcpConfigAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        const string script =
            "cd \"$1\" 2>/dev/null || exit 0\n" +
            "if [ ! -e \"$2\" ] && [ -f mcp.json ] && [ ! -e .ymir-mcp-migrated ]; then cp mcp.json \"$2\"; fi\n" +
            ": > .ymir-mcp-migrated\n" +
            "if [ -f \"$2\" ]; then head -c \"$3\" \"$2\"; fi\n" +
            "exit 0\n";
        var spec = new RuntimeProcessSpec(
            "sh",
            ["-c", script, "ymir-read-mcp", PiRuntimeLayout.AgentDirectory, PiRuntimeLayout.UserMcpFileName, (PiExtensionConfig.MaxUserMcpBytes + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        var reader = await runtimeManager.StartProcessAsync(runtimeId, spec, cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            reader.CloseStandardInput();
            using var buffer = new MemoryStream();
            await reader.StandardOutput.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var exitCode = await reader.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Reading user MCP config failed with exit code {exitCode}: {reader.GetStandardErrorTail()}");
            }

            if (buffer.Length == 0)
            {
                return null;
            }

            // 超過上限時不解析，回傳無效內容讓呼叫端記錄並忽略。
            return buffer.Length > PiExtensionConfig.MaxUserMcpBytes ? "<too large>" : Encoding.UTF8.GetString(buffer.ToArray());
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

    private async Task EnsureConfigProvisionedAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var runtimeId = request.RuntimeId;
        var modelsJson = PiModelsConfig.Build(_options, models.Models, request.ThinkingLevel == "none" ? request.ModelId : null, request.ThinkingLevel is null ? request.ModelId : null);
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
