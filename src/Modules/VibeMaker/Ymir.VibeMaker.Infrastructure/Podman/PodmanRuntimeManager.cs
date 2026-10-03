using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Processes;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Podman;

/// <summary>
/// Rootless Podman runtime：一個 workspace 一個長駐 container（主程序 <c>sleep infinity</c>），
/// Agent 以 <c>podman exec -i</c> 執行（ADR-0003）。
/// Sprint 0 狀態只存在記憶體；Sprint 1/4 改存 AGENT_RUNTIME 並加入 idle stop 與 reconciliation。
/// </summary>
internal sealed class PodmanRuntimeManager(IOptions<RuntimeOptions> options, ILogger<PodmanRuntimeManager> logger) : IAgentRuntimeManager
{
    private const string ProviderName = "PODMAN";

    private readonly RuntimeOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, RuntimeInfo> _runtimesByWorkspace = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _workspaceLocks = new();

    public async Task<RuntimeInfo> EnsureRuntimeAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // 同一 workspace 序列化，避免兩個 request 同時建立兩個 container（SA §14）。
        var workspaceLock = _workspaceLocks.GetOrAdd(workspaceId, _ => new SemaphoreSlim(1, 1));
        await workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtimeId = _runtimesByWorkspace.TryGetValue(workspaceId, out var known) ? known.RuntimeId : Guid.NewGuid();
            var state = await InspectAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            switch (state)
            {
                case null:
                    var directories = WorkspaceDirectories.For(_options.WorkspaceRoot, workspaceId);
                    directories.EnsureCreated();
                    await RunPodmanAsync(PodmanCommandBuilder.BuildRunArguments(_options, workspaceId, runtimeId, directories), cancellationToken)
                        .ConfigureAwait(false);
                    logger.LogInformation("Created runtime container for workspace {WorkspaceId}", workspaceId);
                    break;
                case "running":
                    break;
                default:
                    await RunPodmanAsync(PodmanCommandBuilder.BuildStartArguments(workspaceId), cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Started runtime container for workspace {WorkspaceId} (was {State})", workspaceId, state);
                    break;
            }

            var runtime = new RuntimeInfo(runtimeId, workspaceId, ProviderName, PodmanCommandBuilder.ContainerName(workspaceId), _options.Image, RuntimeStatus.Running);
            _runtimesByWorkspace[workspaceId] = runtime;
            return runtime;
        }
        finally
        {
            workspaceLock.Release();
        }
    }

    public async Task StartAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        await RunPodmanAsync(PodmanCommandBuilder.BuildStartArguments(runtime.WorkspaceId), cancellationToken).ConfigureAwait(false);
        _runtimesByWorkspace[runtime.WorkspaceId] = runtime with { Status = RuntimeStatus.Running };
    }

    public async Task StopAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        await RunPodmanAsync(PodmanCommandBuilder.BuildStopArguments(runtime.WorkspaceId), cancellationToken).ConfigureAwait(false);
        _runtimesByWorkspace[runtime.WorkspaceId] = runtime with { Status = RuntimeStatus.Stopped };
    }

    public async Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        // 只移除 container；workspace 與 agent-state 目錄保留（SA §15）。
        await RunPodmanAsync(PodmanCommandBuilder.BuildRemoveArguments(runtime.WorkspaceId), cancellationToken).ConfigureAwait(false);
        _runtimesByWorkspace.TryRemove(runtime.WorkspaceId, out _);
    }

    public async Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        var state = await InspectAsync(runtime.WorkspaceId, cancellationToken).ConfigureAwait(false);
        var status = state switch
        {
            null => RuntimeStatus.NotCreated,
            "running" => RuntimeStatus.Running,
            "created" => RuntimeStatus.Created,
            "exited" or "stopped" => RuntimeStatus.Stopped,
            _ => RuntimeStatus.Error,
        };
        return runtime with { Status = status };
    }

    public Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = Find(runtimeId);
        IRuntimeProcess process = HostProcess.Start(
            _options.PodmanExecutable,
            PodmanCommandBuilder.BuildExecArguments(runtime.WorkspaceId, spec),
            workingDirectory: null,
            environment: spec.Environment);
        return Task.FromResult(process);
    }

    private async Task<string?> InspectAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var (exitCode, output, _) = await ExecutePodmanAsync(PodmanCommandBuilder.BuildInspectStatusArguments(workspaceId), cancellationToken)
            .ConfigureAwait(false);
        return exitCode == 0 ? output.Trim() : null;
    }

    private async Task RunPodmanAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var (exitCode, _, error) = await ExecutePodmanAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            logger.LogError("podman {Command} failed ({ExitCode}): {Error}", arguments[0], exitCode, error);
            throw new InvalidOperationException($"podman {arguments[0]} failed with exit code {exitCode}.");
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecutePodmanAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var process = HostProcess.Start(_options.PodmanExecutable, arguments, workingDirectory: null, environment: null);
        await using (process.ConfigureAwait(false))
        {
            process.CloseStandardInput();
            using var reader = new StreamReader(process.StandardOutput);
            var output = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (exitCode, output, process.GetStandardErrorTail());
        }
    }

    private RuntimeInfo Find(Guid runtimeId) =>
        _runtimesByWorkspace.Values.FirstOrDefault(r => r.RuntimeId == runtimeId)
        ?? throw new InvalidOperationException($"Runtime {runtimeId} not found.");
}
