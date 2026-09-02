using System.Security.Cryptography;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Events.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Courier.Identity.Application;

public class UserManagementService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IConfiguration _configuration;

    public UserManagementService(
        DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchLookupService branchLookupService,
        IPasswordHasher passwordHasher, IOutboxWriter outboxWriter, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _passwordHasher = passwordHasher;
        _outboxWriter = outboxWriter;
        _configuration = configuration;
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

    public Task DeactivateUserAsync(Guid userId, Guid actingUserId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(userId, isActive: false, "user.deactivated", actingUserId, cancellationToken);

    public Task ReactivateUserAsync(Guid userId, Guid actingUserId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(userId, isActive: true, "user.reactivated", actingUserId, cancellationToken);

    // Explicit OrganizationId filter: without it, a caller could deactivate/reactivate another
    // org's user by guessing their id. Same reasoning as ListUserBranchGrantsAsync/RevokeBranchAsync.
    private async Task SetActiveAsync(Guid userId, bool isActive, string auditAction, Guid actingUserId, CancellationToken cancellationToken)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var user = await _dbContext.Set<User>().FirstOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId, cancellationToken)
            ?? throw new UserManagementException("User not found.");
        var before = user.IsActive;
        user.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, auditAction, "User", userId.ToString(),
            Before: before.ToString(), After: isActive.ToString()), cancellationToken);
    }

    public async Task<InviteUserResponse> InviteUserAsync(InviteUserRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var email = request.Email.ToLowerInvariant();
        // Same platform-wide uniqueness check as AuthService.RegisterOrganizationAsync/
        // UserProvisioningService.ProvisionStaffUserAsync — User.Email has no per-org uniqueness.
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new UserManagementException("Email is already registered.");
        }

        // Explicit OrganizationId filter: without it, a caller could invite a user directly into
        // another org's role by guessing its id. Same reasoning as every other lookup this phase.
        var role = await _dbContext.Set<Role>().FirstOrDefaultAsync(r => r.Id == request.RoleId && r.OrganizationId == organizationId, cancellationToken)
            ?? throw new UserManagementException("Role not found.");

        // Unusable on purpose (same DummyPasswordHash technique AuthService already establishes
        // for its own timing-safety hash) — this user cannot log in until AcceptInviteAsync
        // (Task 12) sets a real one.
        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
            FullName = request.FullName,
            Phone = request.Phone,
            IsActive = true
        };
        _dbContext.Set<User>().Add(user);

        var now = DateTimeOffset.UtcNow;
        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches, AssignedAt = now, AssignedByUserId = actingUserId
        });
        _dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = user.Id, BranchId = request.BranchId, OrganizationId = organizationId,
            GrantedAt = now, ExpiresAt = request.ExpiresAt, GrantedByUserId = actingUserId
        });

        var plainTextToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _dbContext.Set<UserInvite>().Add(new UserInvite
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = HashToken(plainTextToken),
            ExpiresAt = now.AddDays(7), AcceptedAt = null
        });

        var organizationName = await _dbContext.Set<Organization>().Where(o => o.Id == organizationId).Select(o => o.Name).FirstAsync(cancellationToken);
        var baseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
        var inviteUrl = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(plainTextToken)}";
        await _outboxWriter.WriteAsync(organizationId, "user.invited", new
        {
            RecipientEmail = request.Email,
            FullName = request.FullName,
            OrganizationName = organizationName,
            InviteUrl = inviteUrl
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "user.invited", "User", user.Id.ToString(),
            After: $"Email={request.Email},RoleId={request.RoleId}"), cancellationToken);

        return new InviteUserResponse(user.Id, request.Email);
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
