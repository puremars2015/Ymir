using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Processes;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Containers;

/// <summary>
/// Container runtime（Rootless Podman；開發 / 驗證時可用 Docker，ADR-0005）：一個使用者一個長駐 container（ADR-0007）（主程序 <c>sleep infinity</c>），
/// Agent 以 <c>exec -i</c> 執行（ADR-0003）。
/// Sprint 0 狀態只存在記憶體；Sprint 1/4 改存 AGENT_RUNTIME 並加入 idle stop 與 reconciliation。
/// </summary>
internal sealed class ContainerRuntimeManager(IOptions<RuntimeOptions> options, ILogger<ContainerRuntimeManager> logger) : IAgentRuntimeManager
{
    private readonly RuntimeOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, RuntimeInfo> _runtimesByUser = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    public async Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, CancellationToken cancellationToken)
    {
        // 同一使用者序列化，避免兩個 request 同時建立兩個 container（SA §14）。
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtimeId = _runtimesByUser.TryGetValue(userId, out var known) ? known.RuntimeId : Guid.NewGuid();
            var state = await InspectAsync(userId, cancellationToken).ConfigureAwait(false);
            var transition = state switch
            {
                null => RuntimeTransition.Created,
                "running" => RuntimeTransition.None,
                _ => RuntimeTransition.Started,
            };
            switch (state)
            {
                case null:
                    var directories = UserDirectories.For(_options.WorkspaceRoot, userId);
                    directories.EnsureCreated();
                    if (_options.Provider == RuntimeProvider.Docker)
                    {
                        await RunContainerCliAsync(ContainerCommandBuilder.BuildDockerPrepareMountsArguments(_options, directories), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await RunContainerCliAsync(ContainerCommandBuilder.BuildRunArguments(_options, userId, runtimeId, directories), cancellationToken)
                        .ConfigureAwait(false);
                    logger.LogInformation("Created runtime container for user {UserId}", userId);
                    break;
                case "running":
                    break;
                default:
                    await RunContainerCliAsync(ContainerCommandBuilder.BuildStartArguments(userId), cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Started runtime container for user {UserId} (was {State})", userId, state);
                    break;
            }

            var runtime = new RuntimeInfo(runtimeId, userId, ProviderName(), ContainerCommandBuilder.ContainerName(userId), _options.Image, RuntimeStatus.Running);
            _runtimesByUser[userId] = runtime;
            return runtime with { Transition = transition };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task StartAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        await RunContainerCliAsync(ContainerCommandBuilder.BuildStartArguments(runtime.UserId), cancellationToken).ConfigureAwait(false);
        _runtimesByUser[runtime.UserId] = runtime with { Status = RuntimeStatus.Running };
    }

    public async Task StopAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        await RunContainerCliAsync(ContainerCommandBuilder.BuildStopArguments(runtime.UserId), cancellationToken).ConfigureAwait(false);
        _runtimesByUser[runtime.UserId] = runtime with { Status = RuntimeStatus.Stopped };
    }

    public async Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        // 只移除 container；使用者的 workspace 與 agent-state 目錄保留（SA §15）。
        await RunContainerCliAsync(ContainerCommandBuilder.BuildRemoveArguments(runtime.UserId), cancellationToken).ConfigureAwait(false);
        _runtimesByUser.TryRemove(runtime.UserId, out _);
    }

    public async Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        var state = await InspectAsync(runtime.UserId, cancellationToken).ConfigureAwait(false);
        return runtime with { Status = StatusOf(state) };
    }

    private static RuntimeStatus StatusOf(string? state) => state switch
    {
        null => RuntimeStatus.NotCreated,
        "running" => RuntimeStatus.Running,
        "created" => RuntimeStatus.Created,
        "exited" or "stopped" => RuntimeStatus.Stopped,
        _ => RuntimeStatus.Error,
    };

    public async Task<RuntimeStatus> GetStatusForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        StatusOf(await InspectAsync(userId, cancellationToken).ConfigureAwait(false));

    public async Task<bool> StopForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // 與 EnsureRuntime 共用同一個 lock，避免停止到剛啟動、正要執行程序的 container。
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await InspectAsync(userId, cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                _runtimesByUser.TryRemove(userId, out _);
                return false;
            }

            if (state == "running")
            {
                await RunContainerCliAsync(ContainerCommandBuilder.BuildStopArguments(userId), cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Stopped runtime container for user {UserId}", userId);
            }

            if (_runtimesByUser.TryGetValue(userId, out var known))
            {
                _runtimesByUser[userId] = known with { Status = RuntimeStatus.Stopped };
            }

            return true;
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = Find(runtimeId);
        var execArguments = ContainerCommandBuilder.BuildExecArguments(runtime.UserId, spec); // 先驗證工作目錄
        if (spec.WorkingDirectory != RuntimePaths.Workspace)
        {
            await RunContainerCliAsync(ContainerCommandBuilder.BuildEnsureDirectoryArguments(runtime.UserId, spec.WorkingDirectory), cancellationToken)
                .ConfigureAwait(false);
        }

        return HostProcess.Start(_options.ResolvedExecutable, execArguments, workingDirectory: null, environment: spec.Environment);
    }

    private async Task<string?> InspectAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (exitCode, output, _) = await ExecuteContainerCliAsync(ContainerCommandBuilder.BuildInspectStatusArguments(userId), cancellationToken)
            .ConfigureAwait(false);
        return exitCode == 0 ? output.Trim() : null;
    }

    private async Task RunContainerCliAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var (exitCode, _, error) = await ExecuteContainerCliAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            logger.LogError("{Cli} {Command} failed ({ExitCode}): {Error}", _options.ResolvedExecutable, arguments[0], exitCode, error);
            throw new InvalidOperationException($"{_options.ResolvedExecutable} {arguments[0]} failed with exit code {exitCode}.");
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteContainerCliAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var process = HostProcess.Start(_options.ResolvedExecutable, arguments, workingDirectory: null, environment: null);
        await using (process.ConfigureAwait(false))
        {
            process.CloseStandardInput();
            using var reader = new StreamReader(process.StandardOutput);
            var output = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var exitCode = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (exitCode, output, process.GetStandardErrorTail());
        }
    }

    private string ProviderName() => _options.Provider == RuntimeProvider.Docker ? "DOCKER" : "PODMAN";

    private RuntimeInfo Find(Guid runtimeId) =>
        _runtimesByUser.Values.FirstOrDefault(r => r.RuntimeId == runtimeId)
        ?? throw new InvalidOperationException($"Runtime {runtimeId} not found.");
}
