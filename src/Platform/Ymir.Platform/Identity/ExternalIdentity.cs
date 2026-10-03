namespace Ymir.Platform.Identity;

/// <summary>
/// 由企業 IdP 驗證後得到的身分。唯一鍵是 (<see cref="Issuer"/>, <see cref="Subject"/>)，
/// 不使用帳號名稱或 UPN（可能更名）。見 ADR-0002。
/// </summary>
public sealed record ExternalIdentity(
    string Issuer,
    string Subject,
    string DisplayName,
    string? AccountName,
    string? Email,
    string? Department);
