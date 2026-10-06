using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ymir.VibeMaker.Application.Runtime;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Infrastructure.Runtime.Remote;

/// <summary>
/// API 與 runtime host（<c>Ymir.RuntimeHost</c>）之間的協定（ADR-0008），client 與 server 共用。
/// <para>
/// 所有操作都以 user id 為唯一輸入：container 名稱、host 目錄、image、掛載、資源限制全部由 runtime host 自己的設定決定，
/// API 無法要求任何 host 路徑或 container 參數（SA §12）。這是和「把 container runtime socket 掛進 API」最大的差別。
/// </para>
/// </summary>
internal static partial class RuntimeHostProtocol
{
    public const string AuthorizationScheme = "Bearer";

    /// <summary>Token 最短長度；runtime host 與 API 都會在啟動時檢查。</summary>
    public const int MinimumTokenLength = 32;

    public const int MaxArguments = 256;

    public const int MaxEnvironmentVariables = 32;

    /// <summary>程序啟動訊息的大小上限（含 system prompt 以外的所有參數；prompt 經 stdin 傳送）。</summary>
    public const int MaxStartMessageBytes = 256 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string RuntimePath(Guid userId) => $"/v1/users/{userId:N}/runtime";

    public static string StartPath(Guid userId) => $"{RuntimePath(userId)}/start";

    public static string StopPath(Guid userId) => $"{RuntimePath(userId)}/stop";

    /// <summary>WebSocket：stdin / stdout 以 binary frame 傳送，控制訊息以 text frame（JSON）傳送。</summary>
    public static string ProcessPath(Guid userId) => $"{RuntimePath(userId)}/process";

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
            throw new InvalidOperationException($"{settingName} must be at least {MinimumTokenLength} characters (ADR-0008).");
        }
    }

    /// <summary>
    /// 檢查要在 runtime 內啟動的程序。程序本身在使用者的 container 內執行（隔離邊界是 container），
    /// 這裡擋的是會影響 <c>podman exec</c> 參數解析的輸入：以 <c>-</c> 開頭的執行檔、不合法的環境變數名稱、NUL 字元，以及工作目錄。
    /// </summary>
    /// <returns>錯誤訊息（不含輸入內容）；通過時為 null。</returns>
    public static string? Validate(RuntimeProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.Executable) || spec.Executable.StartsWith('-') || spec.Executable.Contains('\0', StringComparison.Ordinal))
        {
            return "Invalid executable.";
        }

        if (spec.Arguments.Count > MaxArguments || spec.Arguments.Any(a => a is null || a.Contains('\0', StringComparison.Ordinal)))
        {
            return "Invalid arguments.";
        }

        var environment = spec.Environment ?? new Dictionary<string, string>();
        if (environment.Count > MaxEnvironmentVariables
            || environment.Any(kv => !EnvironmentVariableName().IsMatch(kv.Key) || kv.Value is null || kv.Value.Contains('\0', StringComparison.Ordinal)))
        {
            return "Invalid environment variables.";
        }

        return RuntimePaths.IsAllowedWorkingDirectory(spec.WorkingDirectory) ? null : "Working directory is not an allowed runtime path.";
    }

    /// <summary>
    /// Cloudflare Tunnel 管理（ADR-0010）：唯一不以 user id 區分的端點。只接受 token 字串，
    /// 檔案位置與要重啟的服務都由 runtime host 的設定決定。
    /// </summary>
    public const string TunnelPath = "/v1/edge/tunnel";

    public const string TunnelTokenPath = "/v1/edge/tunnel-token";

    public const int MinimumTunnelTokenLength = 100;

    public const int MaximumTunnelTokenLength = 4096;

    /// <summary>Cloudflare 的 tunnel token 是 base64 字串；只允許這些字元，寫進 env 檔時就無法注入換行或其他設定。</summary>
    public static bool IsValidTunnelToken(string? token) =>
        token is { Length: >= MinimumTunnelTokenLength and <= MaximumTunnelTokenLength } && TunnelTokenPattern().IsMatch(token);

    // \z 而不是 $：.NET 的 $ 在結尾的 \n 之前就成立，會放行帶換行的 token。
    [GeneratedRegex(@"^[A-Za-z0-9+/=_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TunnelTokenPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentVariableName();
}

/// <summary>Runtime host 的位址：<c>unix:/絕對路徑</c> 或只限 loopback 的 <c>http://</c>（ADR-0008）。</summary>
internal sealed record RuntimeHostEndpoint(string? SocketPath, Uri? HttpUri)
{
    private const string UnixPrefix = "unix:";

    /// <summary>
    /// 容器內代表「主機」的名稱。只有 API（client 端）可以使用：Windows Docker Desktop 無法把 Unix socket 掛進 Linux 容器，
    /// 改由 runtime host 聽主機的 <c>127.0.0.1</c>，API 容器經 <c>host.docker.internal</c> 連線（Docker Desktop 轉送後是 loopback）。
    /// 只用於開發 / 驗證（ADR-0005、ADR-0008）。
    /// </summary>
    private static readonly string[] s_containerHostAliases = ["host.docker.internal", "host.containers.internal"];

    public bool IsUnixSocket => SocketPath is not null;

    /// <summary>是否經由容器的主機別名連線（明文 HTTP 經過容器網路，只限開發）。</summary>
    public bool UsesContainerHostAlias => HttpUri is { } uri && s_containerHostAliases.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <param name="allowContainerHostAlias">client 端（API 容器）為 true；runtime host 監聽的位址一律只能是 loopback。</param>
    public static RuntimeHostEndpoint Parse(string? value, string settingName, bool allowContainerHostAlias = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{settingName} is required (ADR-0008).");
        }

        if (value.StartsWith(UnixPrefix, StringComparison.Ordinal))
        {
            var path = value[UnixPrefix.Length..];
            if (!Path.IsPathRooted(path))
            {
                throw new InvalidOperationException($"{settingName} must use an absolute socket path (unix:/run/...).");
            }

            return new RuntimeHostEndpoint(path, null);
        }

        // 不提供 TLS：TCP 只允許 loopback，避免 runtime host 暴露到網路上。
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback
                || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address))
                || (allowContainerHostAlias && s_containerHostAliases.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)))
            && uri.AbsolutePath == "/")
        {
            return new RuntimeHostEndpoint(null, uri);
        }

        throw new InvalidOperationException($"{settingName} must be unix:/path or a loopback http:// address (ADR-0008).");
    }
}

/// <summary>Runtime 資訊（runtime host 回傳）。</summary>
internal sealed record RuntimeInfoMessage(
    Guid RuntimeId,
    Guid UserId,
    string Provider,
    string ProviderRuntimeId,
    string ImageVersion,
    RuntimeStatus Status)
{
    public static RuntimeInfoMessage From(RuntimeInfo info) =>
        new(info.RuntimeId, info.UserId, info.Provider, info.ProviderRuntimeId, info.ImageVersion, info.Status);

    public RuntimeInfo ToRuntimeInfo() => new(RuntimeId, UserId, Provider, ProviderRuntimeId, ImageVersion, Status);
}

/// <summary>
/// <c>GET /v1/users/{userId}/runtime</c> 的回應：以 user id 查到的實際狀態（沒有 runtime 時為 NotCreated）。
/// 不依賴 runtime host 記憶體中的 runtime id，兩邊服務重新啟動後仍能查詢與停止。
/// </summary>
internal sealed record RuntimeStateMessage(RuntimeStatus Status);

/// <summary>WebSocket 的第一個訊息（text frame）：要啟動的程序。</summary>
internal sealed record ProcessStartMessage(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment,
    string WorkingDirectory)
{
    public static ProcessStartMessage From(RuntimeProcessSpec spec) =>
        new(spec.Executable, spec.Arguments, spec.Environment, spec.WorkingDirectory);

    public RuntimeProcessSpec ToSpec() => new(Executable, Arguments ?? [], Environment, WorkingDirectory ?? RuntimePaths.Workspace);
}

/// <summary>
/// 控制訊息（text frame）。Server → client：<c>started</c>、<c>error</c>、<c>exit</c>；client → server：<c>closeStdin</c>、<c>kill</c>。
/// </summary>
internal sealed record ProcessControlMessage(string Type, int? ExitCode = null, string? StderrTail = null, string? Message = null)
{
    public const string Started = "started";
    public const string Error = "error";
    public const string Exit = "exit";
    public const string CloseStdin = "closeStdin";
    public const string Kill = "kill";
}

/// <summary><c>GET /v1/edge/tunnel</c> 的回應。<paramref name="ManagementEnabled"/> 為 false 表示這台主機沒有開放由網頁管理 tunnel。</summary>
internal sealed record TunnelStatusMessage(bool ManagementEnabled, bool Configured, bool Active, DateTimeOffset? UpdatedAt);

/// <summary><c>PUT /v1/edge/tunnel-token</c> 的 body。</summary>
internal sealed record TunnelTokenMessage(string Token);
