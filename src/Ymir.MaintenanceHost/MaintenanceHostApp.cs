using System.Globalization;
using System.Net;
using Ymir.Maintenance;

namespace Ymir.MaintenanceHost;

/// <summary>
/// 主機維護服務（ADR-0025 §2）：獨立、最小權限的資源監控與安全自動回收。需要讀取 root 的 Podman store 並以指定帳號查詢 rootless store，
/// 所以以 root 執行；root 權限只在這個服務，runtime host 維持 rootless，API 也不掛載任何 container runtime socket（ADR-0008）。
/// </summary>
public static class MaintenanceHostApp
{
    public static WebApplication Build(string[] args) => Build(args, configureServices: null);

    /// <param name="configureServices">測試用：在預設註冊之後替換服務（例如假的 container CLI 與主機資源）。</param>
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configureServices)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = builder.Configuration.GetSection(MaintenanceHostOptions.SectionName).Get<MaintenanceHostOptions>() ?? new MaintenanceHostOptions();
        options.Validate();
        var endpoint = MaintenanceEndpoint.Parse(options.Listen, "MaintenanceHost:Listen");
        var socketMode = ParseSocketMode(options.SocketMode);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (endpoint.SocketPath is { } socketPath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }

                kestrel.ListenUnixSocket(socketPath);
            }
            else
            {
                var uri = endpoint.HttpUri!;
                var address = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) ? ip : IPAddress.Loopback;
                kestrel.Listen(address, uri.Port);
            }
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IContainerCli>(sp => new ProcessContainerCli(options.ContainerExecutable, sp.GetRequiredService<ILogger<ProcessContainerCli>>()));
        builder.Services.AddSingleton<IHostMetricsSource, ProcHostMetricsSource>();
        builder.Services.AddSingleton(_ => new MaintenanceStateStore(options.StateDirectory, options.MaxRunRecords));
        builder.Services.AddSingleton<StoreScanner>();
        builder.Services.AddSingleton<MaintenanceService>();
        builder.Services.AddHostedService<MaintenanceWorker>();
        configureServices?.Invoke(builder.Services);

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

        app.UseMiddleware<MaintenanceTokenMiddleware>();
        app.MapGet("/health", () => Results.Text("ok"));
        app.MapMaintenanceEndpoints();
        return app;
    }

    internal static UnixFileMode ParseSocketMode(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "660" : value.Trim();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _) || text.Length > 4 || text.Any(c => c > '7'))
        {
            throw new InvalidOperationException("MaintenanceHost:SocketMode must be an octal mode such as 660.");
        }

        var mode = (UnixFileMode)Convert.ToInt32(text, 8);
        if ((mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
        {
            throw new InvalidOperationException("MaintenanceHost:SocketMode must not grant access to other users.");
        }

        return mode;
    }
}
