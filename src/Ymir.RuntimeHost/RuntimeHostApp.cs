using System.Globalization;
using System.Net;
using Ymir.VibeMaker.Infrastructure;
using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>
/// Runtime host（ADR-0008）：在主機上代替 API 管理每位使用者的 Agent container。API 在容器內執行，
/// 只能透過這個服務要求「為 user X 確保 runtime / 在 runtime 內執行程序」；image、掛載、資源限制、host 路徑全部由這裡的設定決定。
/// <list type="bullet">
/// <item>只聽 Unix socket（建議）或 loopback TCP，每個請求都要 bearer token。</item>
/// <item>以 rootless Podman 的專用帳號執行；不提供任何接受路徑、image 或 container 參數的端點。</item>
/// </list>
/// </summary>
public static class RuntimeHostApp
{
    public const string SectionName = "RuntimeHost";

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = builder.Configuration.GetSection(SectionName).Get<RuntimeHostOptions>() ?? new RuntimeHostOptions();
        RuntimeHostProtocol.EnsureTokenIsStrong(options.Token, "RuntimeHost:Token");
        var endpoint = RuntimeHostEndpoint.Parse(options.Listen, "RuntimeHost:Listen");
        var socketMode = ParseSocketMode(options.SocketMode);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (endpoint.SocketPath is { } socketPath)
            {
                // 上一次程序結束時留下的 socket 檔會讓 bind 失敗。
                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }

                kestrel.ListenUnixSocket(socketPath);
            }
            else
            {
                // RuntimeHostEndpoint 已確認是 loopback；"localhost" 綁 127.0.0.1。
                var uri = endpoint.HttpUri!;
                var address = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) ? ip : IPAddress.Loopback;
                kestrel.Listen(address, uri.Port);
            }
        });

        builder.Services.AddSingleton(new RuntimeHostSettings(options.Token!));
        builder.Services.AddVibeMakerRuntimeManager(builder.Configuration, builder.Environment.IsDevelopment());
        builder.Services.AddSingleton<RuntimeRegistry>();
        builder.Services.AddSingleton<ProcessBridge>();

        var app = builder.Build();
        if (endpoint.SocketPath is { } path)
        {
            // 只有 owner 與 group（API 容器以這個 group 存取）可以連線；token 是第二道防線。
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, socketMode);
                }
            });
        }

        app.UseMiddleware<TokenAuthenticationMiddleware>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.MapGet("/health", () => Results.Text("ok"));
        app.MapRuntimeEndpoints();
        return app;
    }

    internal static UnixFileMode ParseSocketMode(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "660" : value.Trim();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _) || text.Length > 4 || text.Any(c => c > '7'))
        {
            throw new InvalidOperationException("RuntimeHost:SocketMode must be an octal mode such as 660.");
        }

        var mode = (UnixFileMode)Convert.ToInt32(text, 8);
        // 不允許 other 存取：任何本機使用者都能連線就失去 socket 權限這一層保護。
        if ((mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
        {
            throw new InvalidOperationException("RuntimeHost:SocketMode must not grant access to other users.");
        }

        return mode;
    }
}

/// <summary>設定區段 <c>RuntimeHost</c>。Token 只放在部署環境的 secret（例如 systemd EnvironmentFile），不得進版控。</summary>
public sealed class RuntimeHostOptions
{
    /// <summary><c>unix:/run/ymir-runtime/runtime.sock</c> 或 loopback 的 <c>http://127.0.0.1:5090</c>。</summary>
    public string? Listen { get; set; }

    public string? Token { get; set; }

    /// <summary>Unix socket 的權限（八進位），預設 660；不允許 other 存取。</summary>
    public string? SocketMode { get; set; }
}

internal sealed record RuntimeHostSettings(string Token);
