using Ymir.VibeMaker.Domain;

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
    /// 確保使用者有可用的 runtime（一個使用者一個，ADR-0007）：不存在則建立、已停止則啟動、遺失則重建並掛載同一份使用者目錄（SA §15）。
    /// 必須具備 concurrency protection，同一使用者不得同時建立兩個 container（SA §14）。
    /// </summary>
    Task<RuntimeInfo> EnsureRuntimeAsync(Guid userId, CancellationToken cancellationToken);

    Task StartAsync(Guid runtimeId, CancellationToken cancellationToken);

    /// <summary>停止 runtime；使用者目錄與 runtime metadata 保留（SA §15）。</summary>
    Task StopAsync(Guid runtimeId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid runtimeId, CancellationToken cancellationToken);

    Task<RuntimeInfo> GetStatusAsync(Guid runtimeId, CancellationToken cancellationToken);

    /// <summary>
    /// 以 user id 查詢 runtime 的實際狀態；沒有 runtime 時回傳 <see cref="RuntimeStatus.NotCreated"/>。
    /// runtime id 只存在於 manager 的記憶體，服務重新啟動後就會不同（資料庫紀錄的 id 不變），
    /// 所以生命週期管理（閒置停止、啟動時對帳、Admin 停止）一律以 user id 操作（一人一個 runtime，ADR-0007）。
    /// </summary>
    Task<RuntimeStatus> GetStatusForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>以 user id 停止 runtime（檔案保留，SA §15）；沒有 runtime 時回傳 false。</summary>
    Task<bool> StopForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// 在 runtime 內啟動程序（Podman 為 <c>podman exec -i</c>），工作目錄為 <see cref="RuntimeProcessSpec.WorkingDirectory"/>
    /// （不存在則先建立；不在允許清單內則拒絕）。
    /// </summary>
    Task<IRuntimeProcess> StartProcessAsync(Guid runtimeId, RuntimeProcessSpec spec, CancellationToken cancellationToken);
}
