using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Ymir.Platform.Identity;
using Ymir.Platform.Users;

namespace Ymir.Platform.Infrastructure.Identity;

internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && TryGetUserId(out _);

    public Guid UserId => TryGetUserId(out var id) ? id : throw new InvalidOperationException("No authenticated user.");

    public UserRole Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : UserRole.User;

    public string ActorName => IsAuthenticated ? $"user:{UserId:D}" : "anonymous";

    private bool TryGetUserId(out Guid id)
    {
        id = Guid.Empty;
        return Guid.TryParse(Principal?.FindFirstValue(YmirClaims.UserId), out id);
    }
}
