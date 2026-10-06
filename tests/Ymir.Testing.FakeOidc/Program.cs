namespace Ymir.Testing.FakeOidc;

/// <summary>
/// 獨立執行：<c>dotnet run --project tests/Ymir.Testing.FakeOidc</c>（預設 <c>http://127.0.0.1:5299</c>，可用 <c>--urls</c> 改）。
/// client id / secret 由環境變數 <c>FAKE_OIDC_CLIENT_ID</c>、<c>FAKE_OIDC_CLIENT_SECRET</c> 指定（預設 <c>ymir-dev</c> / <c>ymir-dev-secret</c>）。
/// </summary>
internal static class FakeOidcProgram
{
    public static async Task Main(string[] args)
    {
        var urlIndex = Array.IndexOf(args, "--urls");
        var url = urlIndex >= 0 && urlIndex + 1 < args.Length
            ? args[urlIndex + 1]
            : Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';')[0] ?? "http://127.0.0.1:5299";
        var settings = new FakeOidcSettings(
            Environment.GetEnvironmentVariable("FAKE_OIDC_CLIENT_ID") ?? "ymir-dev",
            Environment.GetEnvironmentVariable("FAKE_OIDC_CLIENT_SECRET") ?? "ymir-dev-secret");

        await using var server = await FakeOidcServer.StartAsync(settings, url);
        Console.WriteLine($"Fake OIDC listening; authority: {server.Authority} (client id: {settings.ClientId})");

        var done = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.TrySetResult();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => done.TrySetResult();
        await done.Task;
    }
}
