using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Runtime;

/// <param name="UserId">Runtime 屬於使用者：一個使用者一個 runtime（ADR-0007）。</param>
/// <param name="ProviderRuntimeId">Provider 端的識別，例如 Podman container 名稱。</param>
public sealed record RuntimeInfo(
    Guid RuntimeId,
    Guid UserId,
    string Provider,
    string ProviderRuntimeId,
    string ImageVersion,
    RuntimeStatus Status);
