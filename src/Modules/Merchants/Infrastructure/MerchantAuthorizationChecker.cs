using System.Security.Claims;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Infrastructure;

public class MerchantAuthorizationChecker : IMerchantAuthorizationChecker
{
    private readonly DbContext _dbContext;

    public MerchantAuthorizationChecker(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        var merchantUserId = user.GetUserId();
        if (merchantUserId is null)
        {
            return false;
        }

        var merchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == merchantUserId && mu.IsActive, cancellationToken);
        if (merchantUser is null)
        {
            return false;
        }

        return MerchantPermissionCatalog.RolePermissions[merchantUser.Role].Contains(permission);
    }
}
