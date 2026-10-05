using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;
using Ymir.VibeMaker.Infrastructure.Processes;

namespace Ymir.VibeMaker.Infrastructure.Runtime;

/// <summary>
/// 開發用 runtime：程序直接在 host 執行，<b>沒有任何隔離</b>，只能在 Development 環境使用。
/// Runtime 內部路徑（<c>/workspace</c>、<c>/agent-state</c>）會被改寫成該使用者的 host 目錄（ADR-0007）。
/// Sprint 0 狀態只存在記憶體；Sprint 1 起改存 AGENT_RUNTIME。
/// </summary>
internal sealed class LocalRuntimeManager : IAgentRuntimeManager
{
    private const string ProviderName = "LOCAL";

    private readonly RuntimeOptions _options;
    private readonly ConcurrentDictionary<Guid, RuntimeInfo> _runtimesByUser = new();

    public LocalRuntimeManager(IOptions<RuntimeOptions> options, ILogger<LocalRuntimeManager> logger)
    {
        _options = options.Value;
        logger.LogWarning("Local runtime provider is enabled: agent processes run on the host WITHOUT isolation. Development only.");
    }

    public Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var runtime = _runtimesByUser.GetOrAdd(userId, id =>
        {
            UserDirectories.For(_options.WorkspaceRoot, id).EnsureCreated();
            return new RuntimeInfo(Guid.NewGuid(), id, ProviderName, id.ToString("N"), "local", RuntimeStatus.Running);
        });
        return Task.FromResult(runtime);
    }

    public Task StartAsync(Guid runtimeId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(Guid runtimeId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = Find(runtimeId);
        _runtimesByUser.TryRemove(runtime.UserId, out _);
        return Task.CompletedTask;
    }

    public Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken) => Task.FromResult(Find(runtimeId));

    public Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = Find(runtimeId);
        var directories = UserDirectories.For(_options.WorkspaceRoot, runtime.UserId);
        directories.EnsureCreated();
        var workingDirectory = directories.HostPathOf(spec.WorkingDirectory);
        Directory.CreateDirectory(workingDirectory);

        var environment = spec.Environment?.ToDictionary(kv => kv.Key, kv => MapRuntimePath(kv.Value, directories));
        IRuntimeProcess process = HostProcess.Start(
            spec.Executable,
            spec.Arguments.Select(argument => MapRuntimePath(argument, directories)),
            workingDirectory,
            environment);
        return Task.FromResult(process);
    }

    /// <summary>只改寫「以 runtime 路徑開頭」的值（完全相同或接著 <c>/</c>），其他值原樣保留。</summary>
    internal static string MapRuntimePath(string value, UserDirectories directories)
    {
        foreach (var (runtimePath, hostPath) in new[]
                 {
                     (RuntimePaths.AgentState, directories.AgentState),
                     (RuntimePaths.Workspace, directories.Workspace),
                 })
        {
            if (value == runtimePath)
            {
                return hostPath;
            }

            if (value.StartsWith(runtimePath + "/", StringComparison.Ordinal))
            {
                return hostPath + value[runtimePath.Length..];
            }
        }

        return value;
    }

    private RuntimeInfo Find(Guid runtimeId) =>
        _runtimesByUser.Values.FirstOrDefault(r => r.RuntimeId == runtimeId)
        ?? throw new InvalidOperationException($"Runtime {runtimeId} not found.");
}
