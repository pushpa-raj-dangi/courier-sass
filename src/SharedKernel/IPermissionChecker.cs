using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);
}
