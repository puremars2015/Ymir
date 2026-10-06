namespace Ymir.RuntimeHost;

/// <summary>Runtime host 進入點（ADR-0008）：以 rootless Podman 的專用帳號在主機上執行。</summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        await using var app = RuntimeHostApp.Build(args);
        await app.RunAsync();
    }
}
