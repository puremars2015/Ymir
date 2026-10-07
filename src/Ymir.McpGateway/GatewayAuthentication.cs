using Ymir.VibeMaker.Application.PlatformMcp;

namespace Ymir.McpGateway;

/// <summary>
/// 驗證 <c>/mcp/*</c> 請求的 Bearer token（ADR-0012 B.3）；通過後把 claims 放進 <see cref="HttpContext.Items"/> 給 rate limit 與轉送使用。
/// 失敗一律回 401 摘要，不區分原因。
/// </summary>
internal sealed class GatewayAuthentication(RequestDelegate next, McpGatewayOptions options, TimeProvider timeProvider)
{
    private const string ClaimsKey = "ymir.mcp.claims";

    public static McpGatewayClaims? ClaimsOf(HttpContext context) => context.Items.TryGetValue(ClaimsKey, out var value) ? value as McpGatewayClaims : null;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.Ordinal))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : null;
        if (McpGatewayToken.Validate(options.TokenSigningKey!, token, timeProvider.GetUtcNow()) is not { } claims)
        {
            await McpProxy.WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "平台服務的授權無效或已過期，請重新執行。").ConfigureAwait(false);
            return;
        }

        context.Items[ClaimsKey] = claims;
        await next(context).ConfigureAwait(false);
    }
}
