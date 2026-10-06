using Ymir.VibeMaker.Infrastructure.Runtime.Remote;

namespace Ymir.RuntimeHost;

/// <summary>
/// 每個請求都要 <c>Authorization: Bearer &lt;token&gt;</c>（ADR-0008）；只有 <c>/health</c> 例外（不含任何資訊）。
/// Unix socket 的檔案權限是第一道防線，token 是第二道。
/// </summary>
internal sealed class TokenAuthenticationMiddleware(RequestDelegate next, RuntimeHostSettings settings, ILogger<TokenAuthenticationMiddleware> logger)
{
    private const string Prefix = RuntimeHostProtocol.AuthorizationScheme + " ";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/health", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var presented = header.StartsWith(Prefix, StringComparison.Ordinal) ? header[Prefix.Length..] : null;
        if (!RuntimeHostProtocol.TokenMatches(settings.Token, presented))
        {
            logger.LogWarning("Rejected runtime host request without a valid token: {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
