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

        // A deactivated user holds no permissions. This is what actually revokes access for an
        // already-issued, not-yet-expired access token when a user is disabled mid-session.
        // The tenant query filter on Users still applies, so this can only ever match a user
        // inside the caller's own organization.
        var isActive = await _dbContext.Set<User>()
            .AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (!isActive)
        {
            return false;
        }

        return await _dbContext.Set<Role>()
            .Where(r => _dbContext.Set<UserRole>().Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .SelectMany(r => r.Permissions)
            .AnyAsync(p => p.Key == permission, cancellationToken);
    }
}
