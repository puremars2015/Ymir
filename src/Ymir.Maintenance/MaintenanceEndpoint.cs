namespace Ymir.Maintenance;

/// <summary>
/// 維護服務的位址：<c>unix:/絕對路徑</c>（正式）或 loopback 的 <c>http://127.0.0.1:port</c>（開發 / 測試）。
/// 不接受非 loopback 的 TCP 位址：服務以 root 執行，只能在本機存取（ADR-0025 §2）。
/// </summary>
public sealed record MaintenanceEndpoint(string? SocketPath, Uri? HttpUri)
{
    public static MaintenanceEndpoint Parse(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{settingName} is required (ADR-0025).");
        }

        if (value.StartsWith("unix:", StringComparison.Ordinal))
        {
            var path = value["unix:".Length..];
            if (!Path.IsPathFullyQualified(path) || path.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{settingName} must be unix:/absolute/path.");
            }

            return new MaintenanceEndpoint(path, null);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || uri.Host == "localhost")
            && uri.AbsolutePath == "/")
        {
            return new MaintenanceEndpoint(null, uri);
        }

        throw new InvalidOperationException($"{settingName} must be a unix socket or a loopback http address.");
    }
}
