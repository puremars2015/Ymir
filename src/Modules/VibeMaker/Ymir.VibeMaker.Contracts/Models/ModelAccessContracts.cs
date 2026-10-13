namespace Ymir.VibeMaker.Contracts.Models;

/// <param name="Provider">提供這個模型的供應商；只在管理介面回傳，使用者的 <c>/api/models</c> 不含（ADR-0020 補充）。</param>
public sealed record AdminModelOptionResponse(string Id, string DisplayName, bool SupportsImages, bool Enabled, string? Provider = null);
public sealed record ModelAccessResponse(IReadOnlyList<AdminModelOptionResponse> Models, string? DefaultModelId, bool UsesDeployment, DateTimeOffset? UpdatedAt);
public sealed record SaveModelAccessRequest(IReadOnlyList<string> EnabledModelIds, string DefaultModelId);
