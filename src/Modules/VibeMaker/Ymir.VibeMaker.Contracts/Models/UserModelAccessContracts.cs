namespace Ymir.VibeMaker.Contracts.Models;

public sealed record UserModelOptionResponse(string Id, string DisplayName, bool SystemEnabled, bool? Override, bool Enabled);
public sealed record UserModelAccessResponse(IReadOnlyList<UserModelOptionResponse> Models, string? DefaultModelId, bool IsValid, DateTimeOffset? UpdatedAt);
public sealed record SaveUserModelAccessRequest(IReadOnlyDictionary<string, bool> Overrides);
