using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class UserProvisioningService : IUserProvisioningService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public UserProvisioningService(DbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        // Same platform-wide uniqueness check as RegisterOrganizationAsync — bypasses the tenant
        // query filter deliberately, since a duplicate email in ANY org must be rejected (User.Email
        // has no per-org uniqueness, it's globally unique for login lookup).
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new UserProvisioningException("Email is already registered.");
        }

        // IgnoreQueryFilters: this method takes organizationId explicitly and filters on it directly
        // (same as the email-uniqueness check above), rather than relying on the ambient
        // ICurrentTenantProvider matching — callers may invoke this from contexts where the tenant
        // filter isn't populated identically (e.g. registration-adjacent flows), same reasoning as
        // AuthService's own pre-auth queries.
        var role = await _dbContext.Set<Role>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.Name == request.RoleName, cancellationToken)
            ?? throw new UserProvisioningException($"Role '{request.RoleName}' is not a seeded role for this organization.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName,
            Phone = request.Phone
        };
        _dbContext.Set<User>().Add(user);

        var now = DateTimeOffset.UtcNow;
        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = now,
            AssignedByUserId = grantedByUserId
        });
        // Without this, IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync would return an
        // empty set for this user (SpecificBranches scope, zero UserBranch rows) — every
        // branch-scoped permission check the new rider needs would fail immediately.
        _dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BranchId = request.BranchId,
            OrganizationId = organizationId,
            GrantedAt = now,
            GrantedByUserId = grantedByUserId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}
