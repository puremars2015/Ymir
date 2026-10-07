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
}
