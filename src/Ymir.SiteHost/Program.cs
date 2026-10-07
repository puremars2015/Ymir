namespace Ymir.SiteHost;

/// <summary>SiteHost 進入點（ADR-0016）：提供使用者發布的網站，放在 Tunnel 的萬用字元路由後面。</summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        await using var app = SiteHostApp.Build(args);
        await app.RunAsync();
    }
}
