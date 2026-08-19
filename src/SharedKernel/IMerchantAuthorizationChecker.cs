using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IMerchantAuthorizationChecker
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);
}
