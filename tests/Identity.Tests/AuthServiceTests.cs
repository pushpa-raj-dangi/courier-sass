using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Identity.Tests;

file class NullTenantProvider : ICurrentTenantProvider
{
    public Guid? OrganizationId => null;
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NullBranchLookupService : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default)
        => Task.FromResult<BranchZoneInfo?>(null);

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class NoopOrganizationSubscriptionStarter : IOrganizationSubscriptionStarter
{
    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class RecordingOrganizationSubscriptionStarter : IOrganizationSubscriptionStarter
{
    public List<(Guid OrganizationId, Guid ActingUserId)> Calls { get; } = new();
    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        Calls.Add((organizationId, actingUserId));
        return Task.CompletedTask;
    }
}

// Finding 4 (Important, final whole-branch review): simulates StartTrialAsync throwing (e.g. a
// transient DB error) after the org/owner/refresh-token are already durably committed, to prove
// RegisterOrganizationAsync fails OPEN rather than 500ing a caller who has no way to retry.
file class ThrowingOrganizationSubscriptionStarter : IOrganizationSubscriptionStarter
{
    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated failure starting trial subscription.");
}

// Task 4 fix round (Important finding): RegisterOrganizationAsync's IssueRefreshTokenAsync call
// makes OrganizationSettingsService.GetOrCreateAsync persist a brand-new org's OrganizationSettings
// row with its own SaveChangesAsync (Task 1) before RegisterOrganizationAsync's own closing
// SaveChangesAsync runs. That is exactly two SaveChangesAsync calls in sequence for a fresh
// organization. This subclass throws on the second one to simulate a transient failure landing
// between them — the scenario RegisterOrganizationAsync's new explicit transaction exists to
// make safe (either both commit, or neither does).
file sealed class FaultOnSecondSaveDbContext : AppDbContext
{
    private int _saveCallCount;

    public FaultOnSecondSaveDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider tenantProvider)
        : base(options, tenantProvider)
    {
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _saveCallCount++;
        if (_saveCallCount == 2)
        {
            throw new InvalidOperationException("Simulated transient failure on the second SaveChangesAsync call.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

public class AuthServiceTests
{
    private static AppDbContext CreateContext() => TestDbContextFactory.Create(new NullTenantProvider());

    private static AuthService CreateService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService(), new NoopOrganizationSubscriptionStarter(), new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger())));
    }

    [Fact]
    public async Task RegisterOrganizationAsync_ThenLoginAsync_Succeeds()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var registerResult = await service.RegisterOrganizationAsync(request);
        Assert.NotEqual(Guid.Empty, registerResult.UserId);

        var loginResult = await service.LoginAsync(new LoginRequest(request.OwnerEmail, "P@ssw0rd123!"));
        Assert.Equal(registerResult.UserId, loginResult.UserId);
        Assert.Equal(registerResult.OrganizationId, loginResult.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(loginResult.RefreshToken));
    }

    [Fact]
    public async Task RegisterOrganizationAsync_Seeds14Roles_WithExpectedPermissionSets()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var result = await service.RegisterOrganizationAsync(request);

        // IgnoreQueryFilters(): this test's DbContext uses NullTenantProvider (matching every
        // other test in this file, since AuthService's own pre-auth methods rely on the same
        // bypass) — the ambient tenant filter fails CLOSED with no tenant in context, so a plain
        // Set<Role>() query here would return 0 rows regardless of what was actually seeded.
        var roles = await dbContext.Set<Role>().IgnoreQueryFilters()
            .Where(r => r.OrganizationId == result.OrganizationId)
            .Include(r => r.Permissions)
            .ToListAsync();
        Assert.Equal(14, roles.Count);
        Assert.All(roles, r => Assert.True(r.IsSystemDefault));

        var financeManager = roles.Single(r => r.Name == "FinanceManager");
        Assert.Contains(financeManager.Permissions, p => p.Key == "cod.settle");
        Assert.Contains(financeManager.Permissions, p => p.Key == "cod.adjust");

        var financeStaff = roles.Single(r => r.Name == "FinanceStaff");
        Assert.DoesNotContain(financeStaff.Permissions, p => p.Key == "cod.adjust");

        var auditor = roles.Single(r => r.Name == "Auditor");
        Assert.All(auditor.Permissions, p => Assert.True(p.Key.EndsWith(".read") || p.Key == "audit.read"));

        var organizationAdmin = roles.Single(r => r.Name == "OrganizationAdmin");
        Assert.DoesNotContain(organizationAdmin.Permissions, p => p.Key == "org.billing.manage");
        Assert.DoesNotContain(organizationAdmin.Permissions, p => p.Key == "org.ownership.transfer");
        Assert.DoesNotContain(organizationAdmin.Permissions, p => p.Key == "org.delete");
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        await service.RegisterOrganizationAsync(request);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.LoginAsync(new LoginRequest(request.OwnerEmail, "wrong-password")));
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatesToken()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registerResult = await service.RegisterOrganizationAsync(request);

        var refreshed = await service.RefreshTokenAsync(new RefreshRequest(registerResult.RefreshToken));

        Assert.Equal(registerResult.UserId, refreshed.UserId);
        Assert.NotEqual(registerResult.RefreshToken, refreshed.RefreshToken);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.RefreshTokenAsync(new RefreshRequest(registerResult.RefreshToken)));
    }

    [Fact]
    public async Task RegisterOrganizationAsync_StartsTrialSubscriptionForNewOrganization()
    {
        await using var dbContext = CreateContext();
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        var subscriptionStarter = new RecordingOrganizationSubscriptionStarter();
        var service = new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService(), subscriptionStarter, new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger())));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var result = await service.RegisterOrganizationAsync(new RegisterOrganizationRequest(
            $"Trial Org {suffix}", $"trial-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));

        var call = Assert.Single(subscriptionStarter.Calls);
        Assert.Equal(result.OrganizationId, call.OrganizationId);
        Assert.Equal(result.UserId, call.ActingUserId);
    }

    // Finding 4 (Important, final whole-branch review): StartTrialAsync throwing must not turn
    // into a 500 for a caller whose org/owner/refresh-token are already durably committed — the
    // registration must still succeed and return a usable access token.
    [Fact]
    public async Task RegisterOrganizationAsync_SubscriptionStarterThrows_StillSucceedsWithAccessToken()
    {
        await using var dbContext = CreateContext();
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        var service = new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService(), new ThrowingOrganizationSubscriptionStarter(), new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger())));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var result = await service.RegisterOrganizationAsync(new RegisterOrganizationRequest(
            $"Resilient Org {suffix}", $"resilient-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));

        Assert.NotEqual(Guid.Empty, result.UserId);
        Assert.NotEqual(Guid.Empty, result.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));

        // The org/owner really were committed despite the throw — a fresh login works too.
        var loginResult = await service.LoginAsync(new LoginRequest($"owner-{suffix}@example.com", "P@ssw0rd123!"));
        Assert.Equal(result.UserId, loginResult.UserId);
    }

    [Fact]
    public async Task LoginAsync_UsesOrganizationConfiguredRefreshTokenLifetime()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerRequest = new RegisterOrganizationRequest($"Org {suffix}", $"org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registered = await service.RegisterOrganizationAsync(registerRequest);

        var settingsService = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger()));
        await settingsService.UpdateSecurityAsync(registered.OrganizationId, Guid.NewGuid(), new SecurityPolicyDto(1, 8));

        var loginResult = await service.LoginAsync(new LoginRequest($"owner-{suffix}@example.com", "P@ssw0rd123!"));

        // RegisterOrganizationAsync above already issued one refresh token under the
        // organization's still-default (7-day) policy, before UpdateSecurityAsync shortened it
        // to 1 day. RefreshToken has no CreatedAt column to pick "the one LoginAsync just issued"
        // by recency, so OrderByDescending(ExpiresAt) would actually return that longer-lived
        // registration token instead of login's new one. Ordering ascending picks the
        // soonest-to-expire token, which is the 1-day one LoginAsync just issued under the
        // updated policy — the one this test means to assert on.
        var token = dbContext.Set<RefreshToken>().OrderBy(t => t.ExpiresAt).First(t => t.UserId == loginResult.UserId);
        Assert.InRange(token.ExpiresAt, DateTimeOffset.UtcNow.AddDays(1).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(1));
    }

    // Task 4 fix round (Important finding): proves RegisterOrganizationAsync's new explicit
    // transaction actually restores the original all-or-nothing write. FaultOnSecondSaveDbContext
    // throws on the second of the two SaveChangesAsync calls this method now makes for a
    // brand-new organization (the first, inside OrganizationSettingsService.GetOrCreateAsync,
    // persists the org/roles/owner/OrganizationSettings; the second persists the refresh token).
    // Without the transaction, the first SaveChangesAsync would have committed independently,
    // leaving a durable organization/owner with no refresh token and no way to retry. With the
    // fix, both are rolled back together.
    [Fact]
    public async Task RegisterOrganizationAsync_FailureBetweenTheTwoSaveChangesCalls_RollsBackEverything()
    {
        // Warm up the shared schema via TestDbContextFactory's lazy migration before the
        // fault-injecting context (built directly, bypassing TestDbContextFactory.Create) touches
        // the database.
        await using (var warmup = CreateContext())
        {
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbContextFactory.GetConnectionString())
            .Options;
        await using var faultyDbContext = new FaultOnSecondSaveDbContext(options, new NullTenantProvider());
        var service = CreateService(faultyDbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Atomicity Org {suffix}", $"atomicity-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterOrganizationAsync(request));

        await using var verifyContext = CreateContext();
        var organizationExists = await verifyContext.Set<Organization>().IgnoreQueryFilters()
            .AnyAsync(o => o.Slug == request.OrganizationSlug);
        Assert.False(organizationExists, "The transaction should have rolled back the organization insert along with everything else.");

        var userExists = await verifyContext.Set<User>().IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.OwnerEmail.ToLowerInvariant());
        Assert.False(userExists, "The transaction should have rolled back the owner user insert along with everything else.");
    }
}
