using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ymir.Maintenance;

/// <summary>
/// API 與主機維護服務（<c>Ymir.MaintenanceHost</c>）之間的協定（ADR-0025 §2），client 與 server 共用。
/// <para>
/// 只有固定的查詢、政策同步、紀錄查詢與 dry run；沒有任何接受路徑、store、image 或 container 名稱的端點。
/// 可管理的範圍、store 帳號與設定檔位置全部由主機的部署設定決定，瀏覽器與 API 都無法指定刪除目標。
/// </para>
/// </summary>
public static class MaintenanceProtocol
{
    public const string AuthorizationScheme = "Bearer";

    /// <summary>Token 最短長度；服務與 API 都會在啟動時檢查（與 runtime host 相同，ADR-0008）。</summary>
    public const int MinimumTokenLength = 32;

    public const string ResourcesPath = "/v1/resources";

    public const string PolicyPath = "/v1/policy";

    public const string RunsPath = "/v1/runs";

    public const string DryRunPath = "/v1/dry-run";

    /// <summary>一次最多回傳的回收紀錄數。</summary>
    public const int MaxRunsPerPage = 100;

    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static string RunsAfterPath(long afterId) => $"{RunsPath}?after={afterId}";

    /// <summary>以 SHA-256 後固定時間比較，長度不同也不會洩漏時間差。</summary>
    public static bool TokenMatches(string expected, string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
    }

    public static void EnsureTokenIsStrong(string? token, string settingName)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < MinimumTokenLength)
        {
            throw new InvalidOperationException($"{settingName} must be at least {MinimumTokenLength} characters (ADR-0025).");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
