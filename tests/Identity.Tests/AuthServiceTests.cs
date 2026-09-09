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
    public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default)
        => Task.FromResult<BranchZoneInfo?>(null);

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
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
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService());
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
}
