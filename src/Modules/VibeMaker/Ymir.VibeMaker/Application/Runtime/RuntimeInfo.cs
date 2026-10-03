using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Runtime;

/// <param name="ProviderRuntimeId">Provider 端的識別，例如 Podman container 名稱。</param>
public sealed record RuntimeInfo(
    Guid RuntimeId,
    Guid WorkspaceId,
    string Provider,
    string ProviderRuntimeId,
    string ImageVersion,
    RuntimeStatus Status);
