using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class UserManagementService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;

    public UserManagementService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchLookupService branchLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
    }

    public async Task<PagedResult<UserListItem>> ListUsersAsync(int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var query = _dbContext.Set<User>().Where(u => u.OrganizationId == organizationId);
        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(u => u.Email)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToArray();
        var roleNamesByUser = await _dbContext.Set<UserRole>()
            .Where(ur => userIds.Contains(ur.UserId) && ur.OrganizationId == organizationId)
            .Join(_dbContext.Set<Role>(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);
        var branchCountsByUser = await _dbContext.Set<UserBranch>()
            .Where(ub => userIds.Contains(ub.UserId) && ub.OrganizationId == organizationId)
            .GroupBy(ub => ub.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var items = users.Select(u => new UserListItem(
            u.Id, u.Email, u.FullName, u.IsActive,
            string.Join(", ", roleNamesByUser.Where(r => r.UserId == u.Id).Select(r => r.Name)) is { Length: > 0 } names ? names : null,
            branchCountsByUser.FirstOrDefault(b => b.UserId == u.Id)?.Count ?? 0)).ToList();

        return new PagedResult<UserListItem>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    // Explicit OrganizationId filter: userId alone is not enough to scope this read — without it,
    // any caller could enumerate another org's user id and read their branch grants. Same
    // defense-in-depth reasoning as RoleService.ListRolesAsync.
    public async Task<IReadOnlyList<UserBranchGrantResponse>> ListUserBranchGrantsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var grants = await _dbContext.Set<UserBranch>()
            .Where(ub => ub.UserId == userId && ub.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        var branchIds = grants.Select(g => g.BranchId).ToHashSet();
        var names = await _branchLookupService.GetNamesByIdsAsync(branchIds, cancellationToken);
        return grants
            .Select(g => new UserBranchGrantResponse(g.BranchId, names.TryGetValue(g.BranchId, out var name) ? name : "Unknown Branch", g.GrantedAt, g.ExpiresAt))
            .ToList();
    }

    public async Task<UserBranchGrantResponse> GrantBranchAsync(Guid userId, GrantBranchRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var now = DateTimeOffset.UtcNow;
        var hasActiveGrant = await _dbContext.Set<UserBranch>()
            .AnyAsync(ub => ub.UserId == userId && ub.BranchId == request.BranchId && (ub.ExpiresAt == null || ub.ExpiresAt > now), cancellationToken);
        if (hasActiveGrant)
        {
            throw new UserManagementException("This user already has an active grant for this branch.");
        }

        var grant = new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BranchId = request.BranchId,
            OrganizationId = organizationId,
            GrantedAt = now,
            ExpiresAt = request.ExpiresAt,
            GrantedByUserId = actingUserId
        };
        _dbContext.Set<UserBranch>().Add(grant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "user.branch_granted", "UserBranch", grant.Id.ToString(),
            After: $"UserId={userId},BranchId={request.BranchId},ExpiresAt={request.ExpiresAt}"), cancellationToken);

        var names = await _branchLookupService.GetNamesByIdsAsync(new HashSet<Guid> { request.BranchId }, cancellationToken);
        return new UserBranchGrantResponse(request.BranchId, names.TryGetValue(request.BranchId, out var name) ? name : "Unknown Branch", grant.GrantedAt, grant.ExpiresAt);
    }

    // Explicit OrganizationId filter: without it, a caller could revoke another org's user's
    // branch grant by guessing a foreign (userId, branchId) pair. Same reasoning as
    // ListUserBranchGrantsAsync above.
    public async Task RevokeBranchAsync(Guid userId, Guid branchId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var grants = await _dbContext.Set<UserBranch>()
            .Where(ub => ub.UserId == userId && ub.BranchId == branchId && ub.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        if (grants.Count == 0)
        {
            throw new UserManagementException("No grant found for this user and branch.");
        }
        _dbContext.Set<UserBranch>().RemoveRange(grants);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "user.branch_revoked", "UserBranch", $"{userId}:{branchId}"), cancellationToken);
    }
}
