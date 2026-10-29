using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.Maintenance;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Processes;
using Ymir.VibeMaker.Infrastructure.Runtime;

namespace Ymir.VibeMaker.Infrastructure.Containers;

/// <summary>
/// Container runtime（Rootless Podman；開發 / 驗證時可用 Docker，ADR-0005）：一個使用者一個長駐 container（ADR-0007）（主程序 <c>ymir-runtime-init</c>，ADR-0034），
/// Agent 以 <c>exec -i</c> 執行（ADR-0003）。
/// Sprint 0 狀態只存在記憶體；Sprint 1/4 改存 AGENT_RUNTIME 並加入 idle stop 與 reconciliation。
/// </summary>
internal sealed class ContainerRuntimeManager(IOptions<RuntimeOptions> options, ILogger<ContainerRuntimeManager> logger) : IAgentRuntimeManager, Health.IRuntimeAvailability
{
    private readonly RuntimeOptions _options = ValidOptions(options.Value);
    private readonly ConcurrentDictionary<Guid, RuntimeInfo> _runtimesByUser = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    public RestrictedNetworkSupport RestrictedNetwork =>
        _options.RestrictedNetwork is null ? RestrictedNetworkSupport.NotConfigured : RestrictedNetworkSupport.Configured;

    public async Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, RuntimeNetworkAccess? network, CancellationToken cancellationToken)
    {
        if (network == RuntimeNetworkAccess.Restricted && _options.RestrictedNetwork is null)
        {
            // 不退回成可以對外連線（ADR-0012 A.8）；既有 container 也不動。
            throw new RuntimeNetworkUnavailableException("Restricted network is not configured (VibeMaker:Runtime:RestrictedNetwork).");
        }

        // 同一使用者序列化，避免兩個 request 同時建立兩個 container（SA §14）。
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var lifecycleLock = await AcquireLifecycleLockAsync(userId, cancellationToken).ConfigureAwait(false);
        try
        {
            var runtimeId = _runtimesByUser.TryGetValue(userId, out var known) ? known.RuntimeId : Guid.NewGuid();
            var inspected = await InspectAsync(userId, cancellationToken).ConfigureAwait(false);
            var state = inspected?.State;
            // 只在呼叫端指定 network 時重建（ExecutionRunner 與對帳自動啟動都持有使用者的執行鎖），
            // 讀檔等其他呼叫不會中斷執行中的 Agent。
            var recreate = network is { } desired && inspected is { } existing
                && (ContainerCommandBuilder.NetworkOfLabel(existing.NetworkLabel) != desired || existing.InitLabel != ContainerCommandBuilder.RuntimeInitLabelValue);
            if (recreate)
            {
                // Network 與主程序只能在建立時決定（ADR-0012 A.8、ADR-0034）：移除 container 後重建，
                // 使用者目錄是掛載的，檔案、Pi session 與登記的背景工作都保留（SA §15）。
                await RunContainerCliAsync(ContainerCommandBuilder.BuildRemoveArguments(userId), cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Removed runtime container for user {UserId} to apply network policy {Network} and the runtime init", userId, network);
                state = null;
            }

            var transition = state switch
            {
                null => recreate ? RuntimeTransition.Recreated : RuntimeTransition.Created,
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

                    await RunContainerCliAsync(
                            ContainerCommandBuilder.BuildRunArguments(_options, userId, runtimeId, directories, network ?? RuntimeNetworkAccess.Internet),
                            cancellationToken)
                        .ConfigureAwait(false);
                    logger.LogInformation("Created runtime container for user {UserId} ({Network})", userId, network ?? RuntimeNetworkAccess.Internet);
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
        using var lifecycleLock = await AcquireLifecycleLockAsync(runtime.UserId, cancellationToken).ConfigureAwait(false);
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
        using var lifecycleLock = await AcquireLifecycleLockAsync(runtime.UserId, cancellationToken).ConfigureAwait(false);
        // 只移除 container；使用者的 workspace 與 agent-state 目錄保留（SA §15）。
        await RunContainerCliAsync(ContainerCommandBuilder.BuildRemoveArguments(runtime.UserId), cancellationToken).ConfigureAwait(false);
        _runtimesByUser.TryRemove(runtime.UserId, out _);
    }

    public async Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        var inspected = await InspectAsync(runtime.UserId, cancellationToken).ConfigureAwait(false);
        return runtime with { Status = StatusOf(inspected?.State) };
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
        StatusOf((await InspectAsync(userId, cancellationToken).ConfigureAwait(false))?.State);

    public async Task<bool> StopForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // 與 EnsureRuntime 共用同一個 lock，避免停止到剛啟動、正要執行程序的 container。
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var lifecycleLock = await AcquireLifecycleLockAsync(userId, cancellationToken).ConfigureAwait(false);
        try
        {
            var state = (await InspectAsync(userId, cancellationToken).ConfigureAwait(false))?.State;
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

    /// <summary>container CLI 能否回應（<c>podman version</c> / <c>docker version</c>）。</summary>
    public async Task<string?> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var (exitCode, _, _) = await ExecuteContainerCliAsync(["version", "--format", "{{.Client.Version}}"], cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? null : $"{ProviderName()} 無法使用";
    }

    /// <summary>container 不存在時回傳 null。</summary>
    /// <summary>
    /// 跨程序的生命週期鎖（ADR-0025 §4）：主機維護服務刪除停止的 runtime 前會取同一把鎖，避免刪到正在建立或啟動的 container。
    /// 未設定或目錄不存在時回傳 null（維護服務同樣因為鎖不可用而不會自動刪除，所以不需要拒絕執行）。
    /// </summary>
    private async Task<IDisposable?> AcquireLifecycleLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_options.LifecycleLockDirectory is not { } directory)
        {
            return null;
        }

        try
        {
            return await LifecycleLock.AcquireAsync(directory, LifecycleLock.UserLockFileName(userId), cancellationToken).ConfigureAwait(false);
        }
        catch (LifecycleLockUnavailableException ex)
        {
            logger.LogWarning(ex, "Lifecycle lock directory is unavailable; continuing with the in-process lock only");
            return null;
        }
    }

    private async Task<(string State, string? NetworkLabel, string? InitLabel)?> InspectAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (exitCode, output, _) = await ExecuteContainerCliAsync(ContainerCommandBuilder.BuildInspectStatusArguments(userId), cancellationToken)
            .ConfigureAwait(false);
        return exitCode == 0 ? ParseInspect(output) : null;
    }

    internal static (string State, string? NetworkLabel, string? InitLabel) ParseInspect(string output)
    {
        var parts = output.Trim().Split('|', 3);
        return (parts[0], LabelAt(parts, 1), LabelAt(parts, 2));
    }

    // Go template 對不存在的 label 輸出空字串或 "<no value>"，都視為沒有 label。
    private static string? LabelAt(string[] parts, int index) =>
        parts.Length > index && parts[index] is { Length: > 0 } value && value != "<no value>" ? value : null;

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

    private static RuntimeOptions ValidOptions(RuntimeOptions options)
    {
        // 受限網路名稱設定錯誤（例如 host）時啟動就拒絕，而不是等到第一次執行（ADR-0012 A.8）。
        options.ValidateRestrictedNetwork();
        return options;
    }

    private string ProviderName() => _options.Provider == RuntimeProvider.Docker ? "DOCKER" : "PODMAN";

    private RuntimeInfo Find(Guid runtimeId) =>
        _runtimesByUser.Values.FirstOrDefault(r => r.RuntimeId == runtimeId)
        ?? throw new InvalidOperationException($"Runtime {runtimeId} not found.");
}
