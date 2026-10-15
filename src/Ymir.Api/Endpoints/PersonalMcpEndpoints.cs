using System.Security.Cryptography;
using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.VibeMaker.Application.Extensions;

namespace Ymir.Api.Endpoints;

internal static class PersonalMcpEndpoints
{
    public static void MapPersonalMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/connections/mcp").WithTags("PersonalConnections").RequireAntiforgeryHeader();
        group.MapGet("", (ICurrentUser user, PersonalMcpService service, CancellationToken ct) => HandleAsync(() => service.ListAsync(user.UserId, ct)))
            .WithName("GetPersonalMcpServers").Produces<PersonalMcpList>();
        group.MapPost("", (AddPersonalMcpRequest request, ICurrentUser user, PersonalMcpService service, CancellationToken ct) => HandleAsync(() => service.AddAsync(user.UserId, user.ActorName, request.Name, request.Url, request.BearerToken, ct)))
            .WithName("AddPersonalMcpServer").Produces<PersonalMcpInfo>();
        group.MapPatch("/{id:guid}", (Guid id, SetPersonalMcpEnabledRequest request, ICurrentUser user, PersonalMcpService service, CancellationToken ct) => HandleAsync(() => service.SetEnabledAsync(user.UserId, user.ActorName, id, request.Enabled, ct)))
            .WithName("SetPersonalMcpEnabled").Produces<PersonalMcpInfo>();
        group.MapPost("/{id:guid}/test", (Guid id, ICurrentUser user, PersonalMcpService service, CancellationToken ct) => HandleAsync(() => service.TestAsync(user.UserId, user.ActorName, id, ct)))
            .WithName("TestPersonalMcpServer").Produces<PersonalMcpTestResult>().RequireRateLimiting("personal-mcp-probe");
        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, PersonalMcpService service, CancellationToken ct) =>
        {
            try
            {
                await service.RemoveAsync(user.UserId, user.ActorName, id, ct);
                return Results.NoContent();
            }
            catch (PersonalMcpException ex) { return Problem(ex); }
        }).WithName("RemovePersonalMcpServer");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (PersonalMcpException ex) { return Problem(ex); }
        catch (CryptographicException) { return ApiProblem.Create(409, "MCP_CREDENTIAL_UNAVAILABLE", "無法解密 MCP 憑證，請移除並重新新增連接。"); }
    }

    private static IResult Problem(PersonalMcpException ex) => ApiProblem.Create(ex.Status, "PERSONAL_MCP_ERROR", ex.Message);
}

public sealed record AddPersonalMcpRequest(string Name, string Url, string? BearerToken = null)
{
    public override string ToString() => "AddPersonalMcpRequest { [REDACTED] }";
}
public sealed record SetPersonalMcpEnabledRequest(bool Enabled);
