using Ymir.Api.Auth;
using Ymir.Api.Problems;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;
using Ymir.VibeMaker.Application.PlatformMcp;
using Ymir.VibeMaker.Domain;

namespace Ymir.Api.Endpoints;

/// <summary>
/// 平台 MCP 服務的存取清單（ADR-0012 B.4、C）：Admin 只能啟用 / 停用與設定對象；服務與位址只在版控的目錄定義，
/// 回應不含後端位址。
/// </summary>
internal static class PlatformMcpEndpoints
{
    public static IEndpointRouteBuilder MapPlatformMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/mcp-servers").WithTags("Admin").RequireAuthorization(AuthSetup.AdminPolicy).RequireAntiforgeryHeader();
        admin.MapGet("/", ListAsync).WithName("AdminListMcpServers");
        admin.MapPut("/{name}/access", SaveAccessAsync).WithName("AdminSaveMcpServerAccess");
        return endpoints;
    }

    private static async Task<PlatformMcpServersResponse> ListAsync(PlatformMcpService service, CancellationToken cancellationToken) =>
        new(service.IsEnabled, [.. (await service.ListAsync(cancellationToken)).Select(McpServerAccessResponse.From)]);

    private static async Task<IResult> SaveAccessAsync(
        string name,
        SaveMcpServerAccessRequest request,
        PlatformMcpService service,
        IUserDirectory users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var userIds = request.UserIds ?? [];
        if (userIds.Count > McpServerAccess.MaxUsers)
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "MCP_ACCESS_INVALID", $"最多指定 {McpServerAccess.MaxUsers} 位使用者。");
        }

        // 只接受存在的使用者，避免存進無意義的 id。
        if (request.Mode == McpAccessMode.SelectedUsers && userIds.Count > 0
            && (await users.FindManyAsync([.. userIds.Distinct()], cancellationToken)).Count != userIds.Distinct().Count())
        {
            return ApiProblem.Create(StatusCodes.Status400BadRequest, "MCP_ACCESS_INVALID", "指定的使用者不存在。");
        }

        if (!await service.SetAccessAsync(name, request.Enabled, request.Mode, userIds, currentUser.ActorName, cancellationToken))
        {
            return ApiProblem.Create(StatusCodes.Status404NotFound, "MCP_SERVER_NOT_FOUND", "服務目錄中沒有這個服務。");
        }

        var state = (await service.ListAsync(cancellationToken)).Single(s => s.Name == name);
        return TypedResults.Ok(McpServerAccessResponse.From(state));
    }
}

/// <param name="Enabled">是否設定了 MCP Gateway（沒有時整個平台 MCP 停用）。</param>
public sealed record PlatformMcpServersResponse(bool Enabled, IReadOnlyList<McpServerAccessResponse> Servers);

public sealed record McpServerAccessResponse(string Name, string Description, bool Enabled, McpAccessMode Mode, IReadOnlyList<Guid> UserIds)
{
    internal static McpServerAccessResponse From(PlatformMcpServerState state) => new(state.Name, state.Description, state.Enabled, state.Mode, state.UserIds);
}

public sealed record SaveMcpServerAccessRequest(bool Enabled, McpAccessMode Mode, IReadOnlyList<Guid>? UserIds);
