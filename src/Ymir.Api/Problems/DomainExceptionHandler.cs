using Microsoft.AspNetCore.Diagnostics;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Problems;

/// <summary>領域驗證失敗 → 400 <c>VALIDATION_FAILED</c>；其他例外交給預設處理（500，不含細節）。</summary>
internal sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainValidationException validation)
        {
            return false;
        }

        await ApiProblem.WriteAsync(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_FAILED", validation.Message);
        return true;
    }
}
