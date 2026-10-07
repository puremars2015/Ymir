namespace Ymir.McpGateway;

/// <summary>MCP Gateway 進入點（ADR-0012 B）：獨立於 API 與 Agent container 的平台服務。</summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        await using var app = McpGatewayApp.Build(args);
        await app.RunAsync();
    }
}
