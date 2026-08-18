using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Infrastructure;

public class BranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly DbContext _dbContext;
    private readonly IBranchHierarchyService _branchHierarchyService;

    public BranchAuthorizationChecker(DbContext dbContext, IBranchHierarchyService branchHierarchyService)
    {
        _dbContext = dbContext;
        _branchHierarchyService = branchHierarchyService;
    }

    public async Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        if (userId is null)
        {
            return new HashSet<Guid>();
        }

        // A deactivated user holds no access, mirroring PermissionChecker.
        var isActive = await _dbContext.Set<User>()
            .AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (!isActive)
        {
            return new HashSet<Guid>();
        }

        var grantingBranchScopes = await _dbContext.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Where(ur => _dbContext.Set<Role>().Any(r => r.Id == ur.RoleId && r.Permissions.Any(p => p.Key == permission)))
            .Select(ur => ur.BranchScope)
            .ToListAsync(cancellationToken);

        if (grantingBranchScopes.Count == 0)
        {
            // User holds no role granting this permission at all.
            return new HashSet<Guid>();
        }

        if (grantingBranchScopes.Contains(BranchScope.AllBranches))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var grantedBranchIds = await _dbContext.Set<UserBranch>()
            .Where(ub => ub.UserId == userId && (ub.ExpiresAt == null || ub.ExpiresAt > now))
            .Select(ub => ub.BranchId)
            .ToListAsync(cancellationToken);

        var accessible = new HashSet<Guid>();
        foreach (var branchId in grantedBranchIds)
        {
            accessible.UnionWith(await _branchHierarchyService.GetDescendantBranchIdsAsync(branchId, cancellationToken));
        }
        return accessible;
    }

    public async Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default)
    {
        var accessible = await GetAccessibleBranchIdsAsync(user, permission, cancellationToken);
        return accessible is null || accessible.Contains(branchId);
    }
}
