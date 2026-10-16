using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ymir.VibeMaker.Application.PlatformMcp;

/// <summary>gateway token 的內容：哪位使用者、可以用哪些服務、何時過期。</summary>
/// <param name="ExecutionId">
/// 簽發這個 token 的 execution（ADR-0030 §2）：內建服務（OneDrive）以它推導工作目錄，並只在 execution 執行中時接受。
/// 外部後端服務看不到這個欄位（gateway 不轉送 token）。
/// </param>
public sealed record McpGatewayClaims(Guid UserId, IReadOnlyList<string> Servers, DateTimeOffset ExpiresAt, Guid? ExecutionId = null);

/// <summary>
/// 每人專屬的短期 gateway token（ADR-0012 B.3）：<c>ymcp1.{payload}.{HMAC-SHA256}</c>，payload 是 base64url JSON。
/// API 簽發、gateway 驗證，兩者共用部署 secret 裡的簽章金鑰；token 本身只以環境變數傳給 Agent 程序，
/// 不出現在程序參數、設定檔或 log。允許的服務在簽發時決定（目錄 ∩ 存取清單），gateway 不需要讀 Ymir 的資料庫。
/// </summary>
public static class McpGatewayToken
{
    public const string Prefix = "ymcp1";
    public const int MinimumKeyLength = 32;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static void EnsureKeyIsStrong(string? key, string settingName)
    {
        if (string.IsNullOrEmpty(key) || key.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException($"{settingName} must be at least {MinimumKeyLength} characters (store it in a deployment secret).");
        }
    }

    public static string Issue(string signingKey, McpGatewayClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        EnsureKeyIsStrong(signingKey, "signing key");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenPayload(claims.UserId, [.. claims.Servers], claims.ExpiresAt.ToUnixTimeSeconds(), claims.ExecutionId), s_json);
        var body = $"{Prefix}.{Base64Url.EncodeToString(payload)}";
        return $"{body}.{Base64Url.EncodeToString(Sign(signingKey, body))}";
    }

    /// <summary>簽章、格式、期限都正確時回傳 claims；任何問題都回傳 null（不區分原因，避免洩漏資訊）。</summary>
    public static McpGatewayClaims? Validate(string signingKey, string? token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 8192)
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return null;
        }

        var body = $"{parts[0]}.{parts[1]}";
        byte[] signature;
        byte[] payload;
        try
        {
            signature = Base64Url.DecodeFromChars(parts[2]);
            payload = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, Sign(signingKey, body)))
        {
            return null;
        }

        TokenPayload? decoded;
        try
        {
            decoded = JsonSerializer.Deserialize<TokenPayload>(payload, s_json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (decoded is null || decoded.Sub == Guid.Empty || decoded.Srv is null)
        {
            return null;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(decoded.Exp);
        return expiresAt <= now ? null : new McpGatewayClaims(decoded.Sub, decoded.Srv, expiresAt, decoded.Exe);
    }

    /// <summary>
    /// gateway 呼叫 API 內建服務（ADR-0030 §2）時的後端憑證：由簽章金鑰衍生，API 與 gateway 都算得出來、Agent 算不出來，
    /// 不需要另一個部署 secret。與 token 簽章用不同的訊息，兩者不能互換。
    /// </summary>
    public static string BuiltInBackendCredential(string signingKey)
    {
        EnsureKeyIsStrong(signingKey, "signing key");
        return Base64Url.EncodeToString(Sign(signingKey, "ymir-mcp-builtin-backend:v1"));
    }

    /// <summary>固定時間比較 <see cref="BuiltInBackendCredential"/>。</summary>
    public static bool IsBuiltInBackendCredential(string signingKey, string? presented) =>
        presented is { Length: > 0 and < 256 }
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(BuiltInBackendCredential(signingKey)));

    private static byte[] Sign(string key, string body) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body));

    private sealed record TokenPayload(Guid Sub, string[]? Srv, long Exp, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? Exe = null);
}
