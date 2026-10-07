using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Ymir.VibeMaker.Application.Connectors.OneDrive;

namespace Ymir.VibeMaker.Infrastructure.Connectors.OneDrive;

/// <summary>
/// refresh token 以 Data Protection 加密（ADR-0013 §2）：金鑰沿用 cookie 與系統設定的那一組
/// （<c>Ymir:DataProtection:KeysPath</c>），資料庫外洩時沒有金鑰無法解密。
/// </summary>
internal sealed class DataProtectionOneDriveTokenProtector(IDataProtectionProvider provider) : IOneDriveTokenProtector
{
    public const string Purpose = "Ymir.Connectors.OneDrive.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public string? Unprotect(string protectedRefreshToken)
    {
        try
        {
            return _protector.Unprotect(protectedRefreshToken);
        }
        catch (CryptographicException)
        {
            // 金鑰遺失或輪替後被刪除：視同需要重新連結。
            return null;
        }
    }
}
