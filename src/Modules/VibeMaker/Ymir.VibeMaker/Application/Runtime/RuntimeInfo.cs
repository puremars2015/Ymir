using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Runtime;

/// <param name="UserId">Runtime 屬於使用者：一個使用者一個 runtime（ADR-0007）。</param>
/// <param name="ProviderRuntimeId">Provider 端的識別，例如 Podman container 名稱。</param>
/// <param name="Transition">這次 EnsureRuntime 是否建立或啟動了 runtime；寫入稽核用（SA §12）。其他操作一律為 None。</param>
public sealed record RuntimeInfo(
    Guid RuntimeId,
    Guid UserId,
    string Provider,
    string ProviderRuntimeId,
    string ImageVersion,
    RuntimeStatus Status,
    RuntimeTransition Transition = RuntimeTransition.None);

/// <summary>EnsureRuntime 對 runtime 做了什麼（SA §12：runtime create / start 要寫稽核）。</summary>
public enum RuntimeTransition
{
    /// <summary>runtime 原本就在執行。</summary>
    None = 0,

    /// <summary>新建立（第一次使用，或 container 遺失後重建）。</summary>
    Created = 1,

    /// <summary>原本已停止，這次啟動。</summary>
    Started = 2,

    /// <summary>原本已存在，但 network 與政策不符，移除後以新的 network 重建（ADR-0012 A.8）；檔案保留。</summary>
    Recreated = 3,
}

/// <summary>
/// Agent container 的對外連線模式（ADR-0012 A.8）。不論哪一種，Agent 都必須連得到 LiteLLM 與 MCP Gateway。
/// </summary>
public enum RuntimeNetworkAccess
{
    /// <summary>沿用部署設定的 network（Podman 預設 slirp4netns、Docker 預設 bridge）：可以對外連線。</summary>
    Internet = 0,

    /// <summary>主機預先建立的 <c>--internal</c> network（<c>VibeMaker:Runtime:RestrictedNetwork</c>），只連得到同網路上的 LiteLLM 與 gateway。</summary>
    Restricted = 1,
}

/// <summary>runtime provider 對受限網路的支援狀態。</summary>
public enum RestrictedNetworkSupport
{
    /// <summary>已設定 <c>VibeMaker:Runtime:RestrictedNetwork</c>，關閉對外連線會生效。</summary>
    Configured,

    /// <summary>沒有設定：政策要求受限網路時 execution 會失敗。</summary>
    NotConfigured,

    /// <summary>由 runtime host 的設定決定，API 無法得知（ADR-0008）。</summary>
    Unknown,

    /// <summary>Local runtime 沒有隔離（只限 Development），不強制。</summary>
    NotEnforced,
}

/// <summary>
/// 政策要求受限網路，但部署沒有設定 <c>VibeMaker:Runtime:RestrictedNetwork</c>（ADR-0012 A.8）。
/// 不退回成可以對外連線；execution 以摘要錯誤失敗，請管理員設定。
/// </summary>
public sealed class RuntimeNetworkUnavailableException(string message) : InvalidOperationException(message);
