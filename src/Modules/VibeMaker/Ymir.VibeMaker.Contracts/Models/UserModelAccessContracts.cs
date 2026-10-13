namespace Ymir.VibeMaker.Contracts.Models;

/// <param name="Provider">提供這個模型的供應商（管理介面用，ADR-0020 補充）。</param>
public sealed record UserModelOptionResponse(string Id, string DisplayName, bool SystemEnabled, bool? Override, bool Enabled, string? Provider = null);
public sealed record UserModelAccessResponse(IReadOnlyList<UserModelOptionResponse> Models, string? DefaultModelId, bool IsValid, DateTimeOffset? UpdatedAt);
public sealed record SaveUserModelAccessRequest(IReadOnlyDictionary<string, bool> Overrides);
