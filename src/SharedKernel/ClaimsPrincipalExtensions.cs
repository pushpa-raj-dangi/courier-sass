using System.Security.Claims;

namespace Courier.SharedKernel;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst("sub")?.Value, out var id) ? id : null;
}
