using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformAuthServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeJwtTokenService : IPlatformJwtTokenService
    {
        public string IssueAccessToken(Guid userId, string email) => $"fake-token-{userId}";
    }

    [Fact]
    public async Task LoginAsync_ValidSuperAdminCredentials_Succeeds()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var email = $"superadmin-{Guid.NewGuid():N}@example.test";
        var user = new User { Id = Guid.NewGuid(), OrganizationId = null, Email = email, PasswordHash = hasher.Hash("correct-password"), FullName = "Test SuperAdmin", IsActive = true };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        try
        {
            var result = await service.LoginAsync(new PlatformLoginRequest(email, "correct-password"));

            Assert.NotNull(result.AccessToken);
            Assert.NotEmpty(result.RefreshToken);
        }
        finally
        {
            // This row's OrganizationId is null by design (that is what makes it a SuperAdmin
            // account), and it is written to the same shared Postgres instance Program.cs's
            // bootstrap block queries against (`Any(u => u.OrganizationId == null)`). Left
            // uncleaned, it would make that bootstrap check see "a SuperAdmin already exists"
            // forever afterward — including for the real API test host, which then never seeds
            // its own configured SuperAdmin and fails PlatformAuthEndpointsTests.
            await CleanupSuperAdminAsync(dbContext, user.Id);
        }
    }

    [Fact]
    public async Task LoginAsync_UserBelongsToOrganization_Rejected()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var email = $"tenant-user-{Guid.NewGuid():N}@example.test";
        // Users.OrganizationId carries a real FK to Organizations (Restrict on delete) — unlike
        // the brief's literal test body, a random unattached Guid here fails the insert with a
        // foreign-key violation against the real Postgres schema, so a real Organization row is
        // required first (same pattern AuthServiceAcceptInviteTests.cs and
        // UserManagementServiceInviteTests.cs already use for this exact reason).
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Tenant Org", Slug = $"tenant-org-{organizationId:N}" });
        dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = email, PasswordHash = hasher.Hash("correct-password"), FullName = "Tenant User", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        await Assert.ThrowsAsync<PlatformAuthenticationFailedException>(() => service.LoginAsync(new PlatformLoginRequest(email, "correct-password")));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_Rejected()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var email = $"superadmin2-{Guid.NewGuid():N}@example.test";
        var user = new User { Id = Guid.NewGuid(), OrganizationId = null, Email = email, PasswordHash = hasher.Hash("correct-password"), FullName = "Test SuperAdmin", IsActive = true };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        try
        {
            await Assert.ThrowsAsync<PlatformAuthenticationFailedException>(() => service.LoginAsync(new PlatformLoginRequest(email, "wrong-password")));
        }
        finally
        {
            // Same reasoning as LoginAsync_ValidSuperAdminCredentials_Succeeds's teardown above.
            await CleanupSuperAdminAsync(dbContext, user.Id);
        }
    }

    private static async Task CleanupSuperAdminAsync(DbContext dbContext, Guid userId)
    {
        var tokens = await dbContext.Set<RefreshToken>().Where(rt => rt.UserId == userId).ToListAsync();
        dbContext.RemoveRange(tokens);
        var user = await dbContext.Set<User>().IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is not null)
        {
            dbContext.Remove(user);
        }
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task LoginAsync_NonexistentEmail_RejectedWithoutThrowingBeforeVerify()
    {
        // Timing-safety regression guard: LoginAsync must run _passwordHasher.Verify(...) against
        // DummyPasswordHash even when no user row exists, rather than returning early the moment
        // user is null. This test only proves the non-existent-email path completes and rejects
        // cleanly (no NullReferenceException from an accidental early dereference of user.*) —
        // actual timing-channel equivalence is a property of the LoginAsync source itself (see
        // the code review checklist), not something a unit test can assert numerically.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        await Assert.ThrowsAsync<PlatformAuthenticationFailedException>(
            () => service.LoginAsync(new PlatformLoginRequest($"nobody-{Guid.NewGuid():N}@example.test", "whatever")));
    }
}
