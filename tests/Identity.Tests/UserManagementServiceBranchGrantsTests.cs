using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Events.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class StubBranchLookupService : IBranchLookupService
{
    public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(branchIds.ToDictionary(id => id, id => $"Branch {id:N}"[..16]));
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

// Only recognizes an explicit allowlist — mirrors IBranchLookupService's real contract of
// returning null for a branch outside the caller's tenant (or nonexistent), which
// GrantBranchAsync/InviteUserAsync now rely on to reject a foreign-org BranchId.
file class SelectiveBranchLookupService : IBranchLookupService
{
    public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly HashSet<Guid> _knownBranchIds;
    public SelectiveBranchLookupService(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(branchIds.ToDictionary(id => id, id => $"Branch {id:N}"[..16]));
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

public class UserManagementServiceBranchGrantsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserBranch>(b =>
            {
                b.HasKey(ub => ub.Id);
            });
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) =>
        CreateService(dbContext, organizationId, new StubBranchLookupService());

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId, IBranchLookupService branchLookupService) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookupService,
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), new ConfigurationBuilder().Build());

    [Fact]
    public async Task GrantBranchAsync_ThenListUserBranchGrantsAsync_ReturnsTheGrant()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);

        var granted = await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());
        Assert.Equal(branchId, granted.BranchId);
        Assert.Null(granted.ExpiresAt);

        var grants = await service.ListUserBranchGrantsAsync(userId);
        var single = Assert.Single(grants);
        Assert.Equal(branchId, single.BranchId);
    }

    [Fact]
    public async Task GrantBranchAsync_WithExpiresAt_PersistsIt()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var granted = await service.GrantBranchAsync(Guid.NewGuid(), new GrantBranchRequest(Guid.NewGuid(), expiresAt), Guid.NewGuid());

        Assert.Equal(expiresAt, granted.ExpiresAt);
    }

    [Fact]
    public async Task GrantBranchAsync_DuplicateActiveGrantForSameBranch_Throws()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, Guid.NewGuid());
        await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid()));
    }

    [Fact]
    public async Task GrantBranchAsync_PriorGrantForSameBranchAlreadyExpired_AllowsNewGrant()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-10), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        var granted = await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());

        Assert.Equal(branchId, granted.BranchId);
    }

    [Fact]
    public async Task RevokeBranchAsync_RemovesTheGrant()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, Guid.NewGuid());
        await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());

        await service.RevokeBranchAsync(userId, branchId, Guid.NewGuid());

        var grants = await service.ListUserBranchGrantsAsync(userId);
        Assert.Empty(grants);
    }

    [Fact]
    public async Task ListUserBranchGrantsAsync_DoesNotReturnAnotherOrganizationsGrant()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = foreignOrganizationId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();
        var callerFromAnotherOrg = CreateService(dbContext, Guid.NewGuid());

        var grants = await callerFromAnotherOrg.ListUserBranchGrantsAsync(userId);

        Assert.Empty(grants);
    }

    [Fact]
    public async Task GrantBranchAsync_UnknownOrForeignOrgBranchId_Throws_AndDoesNotCreateAGrant()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var foreignBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, new SelectiveBranchLookupService()); // no known branches

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.GrantBranchAsync(userId, new GrantBranchRequest(foreignBranchId, null), Guid.NewGuid()));
        Assert.False(await dbContext.Set<UserBranch>().AnyAsync(ub => ub.BranchId == foreignBranchId));
    }

    [Fact]
    public async Task RevokeBranchAsync_AnotherOrganizationsGrant_Throws_AndDoesNotRemoveIt()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var grant = new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = foreignOrganizationId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
        };
        dbContext.Set<UserBranch>().Add(grant);
        await dbContext.SaveChangesAsync();
        var callerFromAnotherOrg = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() => callerFromAnotherOrg.RevokeBranchAsync(userId, branchId, Guid.NewGuid()));
        Assert.True(await dbContext.Set<UserBranch>().AnyAsync(ub => ub.Id == grant.Id));
    }
}
