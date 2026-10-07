namespace Ymir.VibeMaker.Contracts.Models;

public sealed record AdminModelOptionResponse(string Id, string DisplayName, bool SupportsImages, bool Enabled);
public sealed record ModelAccessResponse(IReadOnlyList<AdminModelOptionResponse> Models, string? DefaultModelId, bool UsesDeployment, DateTimeOffset? UpdatedAt);
public sealed record SaveModelAccessRequest(IReadOnlyList<string> EnabledModelIds, string DefaultModelId);
