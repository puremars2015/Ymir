namespace Ymir.Testing.FakeLlm;

/// <summary>
/// 獨立執行：<c>dotnet run --project tests/Ymir.Testing.FakeLlm</c>。
/// 監聽位址依序取 <c>--urls</c>、<c>ASPNETCORE_URLS</c>（Aspire 會設定），預設 <c>http://127.0.0.1:5199</c>。
/// （不使用 top-level statements，避免與 Ymir.Api 的公開 <c>Program</c> 類別衝突。）
/// </summary>
internal static class FakeLlmProgram
{
    public static async Task Main(string[] args)
    {
        var urlIndex = Array.IndexOf(args, "--urls");
        var url = urlIndex >= 0 && urlIndex + 1 < args.Length
            ? args[urlIndex + 1]
            : Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';')[0] ?? "http://127.0.0.1:5199";

        // 設定 FAKE_LLM_MASTER_KEY 時模擬 LiteLLM 的 virtual key 管理（ADR-0004 的本機驗證）。
        var masterKey = Environment.GetEnvironmentVariable("FAKE_LLM_MASTER_KEY");
        await using var server = await FakeLlmServer.StartAsync(url, masterKey);
        Console.WriteLine($"Fake LLM listening on {server.BaseUrl} (model: {FakeLlmEndpoints.ModelId}, key management: {(masterKey is null ? "off" : "on")})");

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
