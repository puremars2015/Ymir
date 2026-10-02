namespace Ymir.VibeMaker.Application.Runtime;

/// <summary>
/// 封裝 Agent 執行環境（MVP 為 Rootless Podman，SA §6.3）。其他模組不得直接依賴 Podman CLI/API。
/// <para>
/// 與 SA §6.3 的差異：SA 的 <c>ExecuteAsync</c> 改為 <see cref="StartProcessAsync"/>，
/// Runtime Manager 只負責「在隔離環境啟動程序」，Agent 協定（Pi RPC）由 <c>IAgentHarness</c> 處理。見 ADR-0003。
/// </para>
/// </summary>
public interface IAgentRuntimeManager
{
    /// <summary>
    /// 確保 workspace 有可用的 runtime：不存在則建立、已停止則啟動、遺失則重建並掛載同一個 workspace（SA §15）。
    /// 必須具備 concurrency protection，同一 workspace 不得同時建立兩個 container（SA §14）。
    /// </summary>
    Task<RuntimeInfo> EnsureRuntimeAsync(Guid workspaceId, CancellationToken cancellationToken);

    Task StartAsync(Guid runtimeId, CancellationToken cancellationToken);

    /// <summary>停止 runtime；workspace 與 runtime metadata 保留（SA §15）。</summary>
    Task StopAsync(Guid runtimeId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken);

    Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken);

    /// <summary>在 runtime 內啟動程序（Podman 為 <c>podman exec -i</c>），工作目錄為 <c>/workspace</c>。</summary>
    Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken);
}
