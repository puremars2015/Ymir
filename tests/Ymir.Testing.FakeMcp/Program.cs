namespace Ymir.Testing.FakeMcp;

/// <summary>本機手動驗證用：<c>dotnet run --project tests/Ymir.Testing.FakeMcp</c>（http://127.0.0.1:5320/mcp）。</summary>
internal static class Program
{
    public static async Task Main()
    {
        await using var server = await FakeMcpServer.StartAsync(Environment.GetEnvironmentVariable("FAKE_MCP_TOKEN"), "http://127.0.0.1:5320");
        Console.WriteLine($"Fake MCP server listening on {server.McpUrl}");
        await Task.Delay(Timeout.Infinite);
    }
}
