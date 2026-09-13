using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class OrganizationSettingsServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private sealed class StubBranchLookupService : IBranchLookupService
    {
        private readonly HashSet<Guid> _existingBranchIds;
        public StubBranchLookupService(params Guid[] existingBranchIds) => _existingBranchIds = existingBranchIds.ToHashSet();
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_existingBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubServiceTypeLookupService : IServiceTypeLookupService
    {
        private readonly HashSet<Guid> _existingIds;
        public StubServiceTypeLookupService(params Guid[] existingIds) => _existingIds = existingIds.ToHashSet();
        public Task<bool> ExistsAsync(Guid serviceTypeId, CancellationToken cancellationToken = default) => Task.FromResult(_existingIds.Contains(serviceTypeId));
    }

    private static Organization NewOrganization(DbContext dbContext)
    {
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().Add(organization);
        return organization;
    }

    [Fact]
    public async Task GetAsync_NoRowExists_CreatesRowWithDefaults()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        var result = await service.GetAsync(organization.Id);

        Assert.Equal("Asia/Kathmandu", result.Profile.TimeZoneId);
        Assert.Equal(7, result.Security.RefreshTokenLifetimeDays);
        Assert.Equal(8, result.Security.MinPasswordLength);
        Assert.Null(result.Defaults.DefaultServiceTypeId);
        var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
        Assert.Equal(7, persisted.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task GetAsync_RowAlreadyExists_ReturnsExistingValuesWithoutOverwriting()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        dbContext.Set<OrganizationSettings>().Add(new OrganizationSettings { OrganizationId = organization.Id, RefreshTokenLifetimeDays = 14, MinPasswordLength = 10, TimeZoneId = "Asia/Kathmandu" });
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        var result = await service.GetAsync(organization.Id);

        Assert.Equal(14, result.Security.RefreshTokenLifetimeDays);
        Assert.Equal(10, result.Security.MinPasswordLength);
        Assert.Single(dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Where(s => s.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task GetSecurityPolicyAsync_NoRowExists_ReturnsDefaultsAndPersistsRow()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        var result = await service.GetSecurityPolicyAsync(organization.Id);

        Assert.Equal(7, result.RefreshTokenLifetimeDays);
        Assert.Equal(8, result.MinPasswordLength);
    }

    [Fact]
    public async Task UpdateProfileAsync_PersistsFieldsAndWritesAuditEntry()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var auditLogger = new FakeAuditLogger();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), auditLogger, new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());
        var actingUserId = Guid.NewGuid();
        var profile = new OrganizationProfileDto("support@example.test", "01-4444444", "New Road, Kathmandu", null, "Asia/Kathmandu", "9:00 AM - 6:00 PM");

        var result = await service.UpdateProfileAsync(organization.Id, actingUserId, profile);

        Assert.Equal("support@example.test", result.SupportEmail);
        var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
        Assert.Equal("support@example.test", persisted.SupportEmail);
        Assert.NotNull(persisted.ProfileUpdatedAt);
        Assert.Equal(actingUserId, persisted.ProfileUpdatedByUserId);
        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("organization.profile_updated", entry.Action);
        Assert.Equal(organization.Id, entry.OrganizationId);
        Assert.Equal(actingUserId, entry.UserId);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(31, 8)]
    [InlineData(7, 7)]
    [InlineData(7, 65)]
    public async Task UpdateSecurityAsync_OutOfBoundsValues_ThrowsValidationException(int refreshDays, int minPasswordLength)
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
            service.UpdateSecurityAsync(organization.Id, Guid.NewGuid(), new SecurityPolicyDto(refreshDays, minPasswordLength)));
    }

    [Fact]
    public async Task UpdateSecurityAsync_ValidValues_PersistsAndWritesAuditEntry()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var auditLogger = new FakeAuditLogger();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), auditLogger, new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());
        var actingUserId = Guid.NewGuid();

        var result = await service.UpdateSecurityAsync(organization.Id, actingUserId, new SecurityPolicyDto(14, 12));

        Assert.Equal(14, result.RefreshTokenLifetimeDays);
        Assert.Equal(12, result.MinPasswordLength);
        var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
        Assert.Equal(14, persisted.RefreshTokenLifetimeDays);
        Assert.Equal(actingUserId, persisted.SecurityUpdatedByUserId);
        Assert.Contains(auditLogger.Entries, e => e.Action == "organization.security_updated");
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_RevokesEveryActiveRefreshTokenForOrganization()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organization.Id, Email = $"user-{Guid.NewGuid():N}@example.test", PasswordHash = "hash", FullName = "Some User", IsActive = true };
        dbContext.Set<User>().Add(user);
        var token = new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = $"hash-{Guid.NewGuid():N}", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) };
        dbContext.Set<RefreshToken>().Add(token);
        await dbContext.SaveChangesAsync();
        var lookupService = new OrganizationAdminService(dbContext, new FakeAuditLogger());
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), lookupService, new StubBranchLookupService(), new StubServiceTypeLookupService());

        await service.RevokeAllSessionsAsync(organization.Id, Guid.NewGuid());

        var reloaded = dbContext.Set<RefreshToken>().Single(rt => rt.Id == token.Id);
        Assert.NotNull(reloaded.RevokedAt);
    }

    [Fact]
    public async Task UpdateDefaultsAsync_UnknownBranch_ThrowsValidationException()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
            service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(null, Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateDefaultsAsync_UnknownServiceType_ThrowsValidationException()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
            service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(Guid.NewGuid(), null)));
    }

    // Pairs an unresolvable service type with a branch id that DOES resolve, proving the
    // service-type validation fires on its own merits rather than only ever being exercised
    // alongside a null (i.e. skipped) branch check.
    [Fact]
    public async Task UpdateDefaultsAsync_ValidBranchButUnknownServiceType_ThrowsValidationException()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var branchId = Guid.NewGuid();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(branchId), new StubServiceTypeLookupService());

        await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
            service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(Guid.NewGuid(), branchId)));
    }

    // Pairs an unresolvable branch with a service type id that DOES resolve, proving the branch
    // validation fires on its own merits rather than only ever being exercised alongside a null
    // (i.e. skipped) service-type check.
    [Fact]
    public async Task UpdateDefaultsAsync_ValidServiceTypeButUnknownBranch_ThrowsValidationException()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var serviceTypeId = Guid.NewGuid();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService(serviceTypeId));

        await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
            service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(serviceTypeId, Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateDefaultsAsync_ValidIds_PersistsAndWritesAuditEntry()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var branchId = Guid.NewGuid();
        var serviceTypeId = Guid.NewGuid();
        var auditLogger = new FakeAuditLogger();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), auditLogger, new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(branchId), new StubServiceTypeLookupService(serviceTypeId));

        var result = await service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(serviceTypeId, branchId));

        Assert.Equal(serviceTypeId, result.DefaultServiceTypeId);
        Assert.Equal(branchId, result.DefaultOriginBranchId);
        Assert.Contains(auditLogger.Entries, e => e.Action == "organization.defaults_updated");
    }

    [Fact]
    public async Task UpdateDefaultsAsync_NullIds_ClearsDefaultsWithoutValidation()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

        var result = await service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(null, null));

        Assert.Null(result.DefaultServiceTypeId);
        Assert.Null(result.DefaultOriginBranchId);
    }
}
