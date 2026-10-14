namespace Ymir.MaintenanceHost;

/// <summary>主機維護服務進入點（ADR-0025）：以 root 在主機上執行（systemd），只聽 Unix socket。</summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        await using var app = MaintenanceHostApp.Build(args);
        await app.RunAsync();
    }
}
