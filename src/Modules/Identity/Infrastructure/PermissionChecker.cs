using System.Security.Claims;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Infrastructure;

public class PermissionChecker : IPermissionChecker
{
    private readonly DbContext _dbContext;

    public PermissionChecker(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        var subClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subClaim, out var userId))
        {
            return false;
        }

        return await _dbContext.Set<Role>()
            .Where(r => _dbContext.Set<UserRole>().Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .SelectMany(r => r.Permissions)
            .AnyAsync(p => p.Key == permission, cancellationToken);
    }
}
