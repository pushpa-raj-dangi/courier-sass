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
    private readonly IOrganizationSubscriptionStarter _organizationSubscriptionStarter;
    private readonly OrganizationSettingsService _organizationSettingsService;

    // Computed exactly once for the lifetime of the process. AuthService is registered as
    // Scoped (one instance per HTTP request), so an instance-level cache would be recomputed
    // on every unknown-email login — making that path cost TWO hashes (compute + verify)
    // against the wrong-password path's one, widening the very timing gap it exists to close.
    // A throwaway hasher is used here because IPasswordHasher is only available per-instance
    // through DI; the DI-injected hasher still performs every real verification.
    private static readonly string DummyPasswordHash =
        new Courier.Identity.Infrastructure.PasswordHasherAdapter().Hash("dummy-password-for-timing-safety");

    public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger, IBranchLookupService branchLookupService, IOrganizationSubscriptionStarter organizationSubscriptionStarter, OrganizationSettingsService organizationSettingsService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _organizationSubscriptionStarter = organizationSubscriptionStarter;
        _organizationSettingsService = organizationSettingsService;
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

        // Transactional: IssueRefreshTokenAsync below calls into OrganizationSettingsService,
        // which — for a brand-new organization with no OrganizationSettings row yet — persists
        // that row with its own SaveChangesAsync (Task 1's GetOrCreateAsync) before this method's
        // own closing SaveChangesAsync runs. Without an explicit transaction spanning both calls,
        // that split registration into two independent commits: a failure in the second call
        // (e.g. a transient DB error) would leave the organization/roles/owner durably committed
        // with no refresh token issued and no way to retry (duplicate email/slug). Wrapping both
        // SaveChangesAsync calls in one transaction restores the original all-or-nothing write.
        // Same IsRelational() guard as CodService.Settlements.cs's CreateRiderSettlementAsync —
        // EF Core's InMemory provider (not used by this module's own tests, which run against
        // real Postgres via TestDbContextFactory, but kept for parity with the established
        // convention) does not support transactions.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        Organization organization;
        User owner;
        (RefreshToken Entity, string PlainText) refreshToken;
        try
        {
            var organizationId = Guid.NewGuid();
            organization = new Organization
            {
                Id = organizationId,
                Name = request.OrganizationName,
                Slug = request.OrganizationSlug,
                AwbPrefix = await AllocateAwbPrefixAsync(request.OrganizationSlug, organizationId, cancellationToken)
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

            // The 11 CLAUDE.md §6 roles not already seeded above (Frontend Phase F9,
            // CLARIFICATIONS.md #18) — same "load once, join tracked Permission rows" pattern as
            // OrganizationOwner/DeliveryRider/PickupRider.
            foreach (var (roleName, permissionKeys) in AdditionalRoleSeeds.ByRoleName)
            {
                var role = new Role
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organization.Id,
                    Name = roleName,
                    IsSystemDefault = true
                };
                _dbContext.Set<Role>().Add(role);
                foreach (var permission in allPermissions.Where(p => permissionKeys.Contains(p.Key)))
                {
                    role.Permissions.Add(permission);
                }
            }

            owner = new User
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

            refreshToken = await IssueRefreshTokenAsync(owner.Id, organization.Id, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organization.Id, owner.Id, "organization.registered", "Organization", organization.Id.ToString()), cancellationToken);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }

        // Every self-registered organization starts on a 7-day trial with no committed Plan —
        // TrialEnforcementMiddleware only blocks writes once TrialExpiryJob flips this to
        // TrialExpired with no real Plan chosen. Called after the main SaveChangesAsync above
        // (not before) so this follows the same "save the primary entities, then record the
        // event" sequencing this method already uses for the "organization.registered" audit
        // entry directly above.
        //
        // Deliberately fails OPEN: the org/owner/refresh-token are already durably committed by
        // the SaveChangesAsync above (and by the audit log's own SaveChangesAsync), so if this
        // throws, failing the whole registration would 500 the caller with no access token and
        // no way to retry (duplicate slug/email) — strictly worse than a trial that silently
        // didn't start. A missing Subscription row is a well-understood, already-supported state
        // in this codebase (every pre-existing organization looks exactly like this, and
        // GetMaxBranchesAsync/IsWriteBlockedAsync already treat "no Subscription" as "unlimited,
        // unblocked"); a SuperAdmin can still assign a Plan directly. Swallowed without logging —
        // AuthService has no ILogger dependency today and this fix intentionally does not widen
        // the class's constructor just to log this one edge case.
        try
        {
            await _organizationSubscriptionStarter.StartTrialAsync(organization.Id, owner.Id, cancellationToken);
        }
        catch (Exception)
        {
        }

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

        // IgnoreQueryFilters(): no tenant is known yet at this pre-authentication point (same
        // reasoning as the User/RefreshToken lookups elsewhere in this class) — AppDbContext's
        // Organization query filter fails closed without it, which would make every login throw
        // instead of only a suspended org's login.
        var organization = await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .FirstAsync(o => o.Id == user.OrganizationId, cancellationToken);
        if (!organization.IsActive || organization.DeletedAt is not null)
        {
            throw new OrganizationSuspendedException("Your organization has been suspended. Contact support.");
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id, user.OrganizationId.Value, cancellationToken);
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

        // Same IgnoreQueryFilters() reasoning as LoginAsync's organization lookup above. Refresh
        // deliberately throws the generic AuthenticationFailedException, not
        // OrganizationSuspendedException — refresh is a silent background call from the
        // frontend's perspective, not the place to surface a specific user-facing message.
        var organization = await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .FirstAsync(o => o.Id == user.OrganizationId!.Value, cancellationToken);
        if (!organization.IsActive || organization.DeletedAt is not null)
        {
            throw new AuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = await IssueRefreshTokenAsync(user.Id, user.OrganizationId!.Value, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId!.Value, user.Email);
        return new AuthResult(accessToken, newRefreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    public async Task<AuthResult> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AuthenticationFailedException("Password is required.");
        }

        var hash = HashToken(request.Token);
        var invite = await _dbContext.Set<UserInvite>().FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invite is null || invite.AcceptedAt is not null || invite.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new AuthenticationFailedException("This invite link is invalid or has expired.");
        }

        // No tenant is known yet at this pre-authentication endpoint — same reasoning as
        // RegisterOrganizationAsync's/LoginAsync's own IgnoreQueryFilters() reads.
        var user = await _dbContext.Set<User>().IgnoreQueryFilters().FirstAsync(u => u.Id == invite.UserId, cancellationToken);

        var securityPolicy = await _organizationSettingsService.GetSecurityPolicyAsync(user.OrganizationId!.Value, cancellationToken);
        if (request.Password.Length < securityPolicy.MinPasswordLength)
        {
            throw new AuthenticationFailedException($"Password must be at least {securityPolicy.MinPasswordLength} characters.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.Password);
        invite.AcceptedAt = DateTimeOffset.UtcNow;

        var refreshToken = await IssueRefreshTokenAsync(user.Id, user.OrganizationId!.Value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(user.OrganizationId, user.Id, "user.invite_accepted", "User", user.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId!.Value, user.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    // pg_advisory_xact_lock(classid, objid) namespace ("AWBP" in ASCII); objid is always 0 — prefix
    // allocation is global, not per-organization.
    private const int AwbPrefixAllocationLockNamespace = 0x41574250;

    // First free AwbPrefix.Candidate for the slug's derived base. Runs inside RegisterOrganizationAsync's
    // transaction; the transaction-scoped advisory lock serializes concurrent registrations so two
    // can never both see the same candidate as free (the unique index on AwbPrefix is the backstop).
    // Organizations is read with IgnoreQueryFilters: registration has no tenant, and uniqueness is
    // across every organization.
    private async Task<string> AllocateAwbPrefixAsync(string slug, Guid organizationId, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_xact_lock({AwbPrefixAllocationLockNamespace}, 0)", cancellationToken);
        }

        var basePrefix = AwbPrefix.Derive(slug, organizationId);
        var taken = (await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .Where(o => o.AwbPrefix.StartsWith(basePrefix.Substring(0, 1)))
            .Select(o => o.AwbPrefix)
            .ToListAsync(cancellationToken)).ToHashSet();
        for (var attempt = 1; ; attempt++)
        {
            var candidate = AwbPrefix.Candidate(basePrefix, attempt);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var securityPolicy = await _organizationSettingsService.GetSecurityPolicyAsync(organizationId, cancellationToken);
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(plainText),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(securityPolicy.RefreshTokenLifetimeDays)
        };
        _dbContext.Set<RefreshToken>().Add(entity);
        return (entity, plainText);
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public async Task<MeResponse> GetMeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userId = principal.GetUserId()!.Value;
        var user = await _dbContext.Set<User>().FirstAsync(u => u.Id == userId, cancellationToken);

        var organizationName = await _dbContext.Set<Organization>()
            .Where(o => o.Id == user.OrganizationId)
            .Select(o => o.Name)
            .FirstAsync(cancellationToken);

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

        var locale = await _organizationSettingsService.GetLocaleAsync(user.OrganizationId!.Value, cancellationToken);

        return new MeResponse(user.Id, user.OrganizationId!.Value, organizationName, user.Email, user.FullName, permissions, hasAllBranchesScope, branches, locale);
    }
}
