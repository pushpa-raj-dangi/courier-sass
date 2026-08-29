using System.Security.Claims;
using System.Security.Cryptography;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class AuthService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;

    // Computed exactly once for the lifetime of the process. AuthService is registered as
    // Scoped (one instance per HTTP request), so an instance-level cache would be recomputed
    // on every unknown-email login — making that path cost TWO hashes (compute + verify)
    // against the wrong-password path's one, widening the very timing gap it exists to close.
    // A throwaway hasher is used here because IPasswordHasher is only available per-instance
    // through DI; the DI-injected hasher still performs every real verification.
    private static readonly string DummyPasswordHash =
        new Courier.Identity.Infrastructure.PasswordHasherAdapter().Hash("dummy-password-for-timing-safety");

    public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger, IBranchLookupService branchLookupService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
    }

    public async Task<AuthResult> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        // Emails are stored and compared in a single canonical (lowercase) form so that
        // "Owner@x.com" and "owner@x.com" are the same account for both the unique index
        // and login. Normalize before the uniqueness check and before persisting.
        var ownerEmail = request.OwnerEmail.ToLowerInvariant();

        // Pre-authentication: no tenant is known yet, so the global org-scoped
        // query filter must be bypassed for this platform-wide uniqueness check.
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters()
            .AnyAsync(u => u.Email == ownerEmail, cancellationToken);
        if (emailTaken)
        {
            throw new AuthenticationFailedException("Email is already registered.");
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.OrganizationName,
            Slug = request.OrganizationSlug
        };
        _dbContext.Set<Organization>().Add(organization);

        var allPermissions = await _dbContext.Set<Permission>().ToListAsync(cancellationToken);
        var ownerRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "OrganizationOwner",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(ownerRole);
        // These Permission rows are already tracked (loaded above), so EF Core inserts
        // only the join-table rows here, not new Permission rows.
        foreach (var permission in allPermissions)
        {
            ownerRole.Permissions.Add(permission);
        }

        // Phase 8: PickupRider/DeliveryRider are seeded here (not lazily on first rider creation)
        // so they exist from org registration onward, matching how OrganizationOwner already works
        // — RiderService (Riders module) looks them up by name via IUserProvisioningService and
        // never creates roles itself. Permission sets per RBAC.md §4 (DeliveryRider is explicit
        // there) and CLARIFICATIONS.md's new Phase 8 entry (PickupRider — not itemized in RBAC.md,
        // resolved to shipment.scan only, since a pickup rider's whole job is the pickup scan).
        var deliveryRiderPermissionKeys = new[] { "delivery.attempt", "delivery.complete", "cod.collect", "shipment.scan" };
        var deliveryRiderRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "DeliveryRider",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(deliveryRiderRole);
        foreach (var permission in allPermissions.Where(p => deliveryRiderPermissionKeys.Contains(p.Key)))
        {
            deliveryRiderRole.Permissions.Add(permission);
        }

        var pickupRiderRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "PickupRider",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(pickupRiderRole);
        foreach (var permission in allPermissions.Where(p => p.Key == "shipment.scan"))
        {
            pickupRiderRole.Permissions.Add(permission);
        }

        var owner = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Email = ownerEmail,
            PasswordHash = _passwordHasher.Hash(request.OwnerPassword),
            FullName = request.OwnerFullName
        };
        _dbContext.Set<User>().Add(owner);

        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            RoleId = ownerRole.Id,
            OrganizationId = organization.Id,
            BranchScope = BranchScope.AllBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = owner.Id
        });

        var refreshToken = await IssueRefreshTokenAsync(owner.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organization.Id, owner.Id, "organization.registered", "Organization", organization.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(owner.Id, organization.Id, owner.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, owner.Id, organization.Id);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Always run a password verification, even when no user was found, so the
        // "unknown email" and "wrong password" paths take the same time (avoids a
        // timing side-channel that would let an attacker enumerate registered emails).
        var passwordValid = _passwordHasher.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);

        // A deactivated account must not be able to log in. The message stays identical to
        // the bad-credentials message so the response does not disclose that the address
        // belongs to a real-but-disabled account.
        if (user is null || !passwordValid || !user.IsActive)
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }
        if (user.OrganizationId is null)
        {
            throw new AuthenticationFailedException("Platform accounts cannot log in through this endpoint.");
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(user.OrganizationId, user.Id, "user.login", "User", user.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId.Value, user.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    public async Task<AuthResult> RefreshTokenAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new AuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstAsync(u => u.Id == existing.UserId, cancellationToken);

        // Deactivating a user must also end their ability to mint fresh access tokens.
        if (!user.IsActive)
        {
            throw new AuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId!.Value, user.Email);
        return new AuthResult(accessToken, newRefreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(plainText),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _dbContext.Set<RefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public async Task<MeResponse> GetMeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userId = principal.GetUserId()!.Value;
        var user = await _dbContext.Set<User>().FirstAsync(u => u.Id == userId, cancellationToken);

        var permissions = await _dbContext.Set<Role>()
            .Where(r => _dbContext.Set<UserRole>().Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .SelectMany(r => r.Permissions)
            .Select(p => p.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        var hasAllBranchesScope = await _dbContext.Set<UserRole>()
            .AnyAsync(ur => ur.UserId == userId && ur.BranchScope == BranchScope.AllBranches, cancellationToken);

        var userBranches = await _dbContext.Set<UserBranch>()
            .Where(ub => ub.UserId == userId)
            .ToListAsync(cancellationToken);

        var branchIds = userBranches.Select(ub => ub.BranchId).ToHashSet();
        var branchNames = await _branchLookupService.GetNamesByIdsAsync(branchIds, cancellationToken);

        var branches = userBranches
            .Select(ub => new MeBranchGrant(
                ub.BranchId,
                branchNames.TryGetValue(ub.BranchId, out var name) ? name : "Unknown Branch",
                ub.ExpiresAt))
            .ToList();

        return new MeResponse(user.Id, user.OrganizationId!.Value, user.Email, user.FullName, permissions, hasAllBranchesScope, branches);
    }
}
