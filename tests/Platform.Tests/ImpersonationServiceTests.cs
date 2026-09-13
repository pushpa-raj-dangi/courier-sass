using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Platform.Tests;

public class ImpersonationServiceTests
{
    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public OrganizationRecord? Organization { get; set; }
        public OrganizationUserRecord? User { get; set; }
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Organization);
        public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationUserRecord>(new List<OrganizationUserRecord>(), 1, 25, 0));
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(User);
        public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(User?.FullName);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeImpersonationTokenService : IImpersonationTokenService
    {
        public static readonly DateTimeOffset FixedExpiresAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);

        public ImpersonationTokenIssued IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId)
            => new("fake-impersonation-token", FixedExpiresAt);
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public AuditEntry? LastEntry { get; private set; }
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { LastEntry = entry; return Task.CompletedTask; }
    }

    [Fact]
    public async Task IssueAsync_ValidActiveUserInOrganization_ReturnsTokenAndAudits()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(organizationId, "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = new OrganizationUserRecord(userId, "user@example.test", "Test User", true)
        };
        var auditLogger = new FakeAuditLogger();
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), auditLogger);

        var result = await service.IssueAsync(organizationId, userId, Guid.NewGuid());

        Assert.Equal("fake-impersonation-token", result.AccessToken);
        Assert.Equal("Test User", result.ImpersonatedUserName);
        Assert.True(auditLogger.LastEntry!.IsCrossTenantAction);
    }

    // Regression guard for the bug where ImpersonationService independently computed
    // DateTimeOffset.UtcNow.AddMinutes(30) instead of using the expiry the token service
    // actually encoded into the JWT's exp claim. ExpiresAt must be exactly what the token
    // service reports, not a value ImpersonationService derives on its own.
    [Fact]
    public async Task IssueAsync_ReturnsExpiresAt_ExactlyAsReportedByTokenService()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(organizationId, "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = new OrganizationUserRecord(userId, "user@example.test", "Test User", true)
        };
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), new FakeAuditLogger());

        var result = await service.IssueAsync(organizationId, userId, Guid.NewGuid());

        Assert.Equal(FakeImpersonationTokenService.FixedExpiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task IssueAsync_InactiveUser_Throws()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(organizationId, "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = new OrganizationUserRecord(userId, "user@example.test", "Test User", false)
        };
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), new FakeAuditLogger());

        await Assert.ThrowsAsync<PlatformValidationException>(() => service.IssueAsync(organizationId, userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task IssueAsync_UserNotInOrganization_Throws()
    {
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(Guid.NewGuid(), "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = null
        };
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), new FakeAuditLogger());

        await Assert.ThrowsAsync<PlatformValidationException>(() => service.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }
}
