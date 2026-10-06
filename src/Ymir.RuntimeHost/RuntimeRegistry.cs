using System.Collections.Concurrent;
using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.RuntimeHost;

/// <summary>
/// user id → runtime id 的對應。API 只傳 user id（ADR-0008），runtime manager 的其他操作需要 runtime id，
/// 由這裡在 <see cref="EnsureAsync"/> 時記下；runtime host 重新啟動後，下一次 EnsureRuntime 會重新建立對應。
/// </summary>
internal sealed class RuntimeRegistry(IAgentRuntimeManager runtimeManager)
{
    private readonly ConcurrentDictionary<Guid, Guid> _runtimeByUser = new();

    public IAgentRuntimeManager Manager => runtimeManager;

    public async Task<RuntimeInfo> EnsureAsync(Guid userId, CancellationToken cancellationToken)
    {
        var runtime = await runtimeManager.EnsureRuntimeAsync(userId, cancellationToken);
        _runtimeByUser[userId] = runtime.RuntimeId;
        return runtime;
    }

    public Guid? Find(Guid userId) => _runtimeByUser.TryGetValue(userId, out var runtimeId) ? runtimeId : null;

    /// <summary>執行程序前使用：沒有對應（例如 runtime host 剛重新啟動）時先確保 runtime。</summary>
    public async Task<Guid> ResolveForProcessAsync(Guid userId, CancellationToken cancellationToken) =>
        Find(userId) ?? (await EnsureAsync(userId, cancellationToken)).RuntimeId;

    public void Forget(Guid userId) => _runtimeByUser.TryRemove(userId, out _);
}
