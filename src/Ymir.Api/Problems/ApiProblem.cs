namespace Ymir.Api.Problems;

/// <summary>錯誤回應一律為 ProblemDetails，並以 <c>code</c> 擴充欄位帶 SA §13 錯誤碼；不含 stack trace 或 host path（SA §12）。</summary>
internal static class ApiProblem
{
    public static IResult Create(int statusCode, string code, string detail) =>
        TypedResults.Problem(detail: detail, statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static Task WriteAsync(HttpContext context, int statusCode, string code, string detail)
    {
        context.Response.StatusCode = statusCode;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = statusCode,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        }).AsTask();
    }
}
