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
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

public class UserManagementServiceListUsersTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => ur.Id);
            modelBuilder.Entity<UserBranch>().HasKey(ub => ub.Id);
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService(),
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), new ConfigurationBuilder().Build());

    private static async Task<User> SeedUserAsync(TestDbContext dbContext, Guid organizationId, string? roleName = null, int branchGrantCount = 0)
    {
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "x", FullName = "Test User" };
        dbContext.Set<User>().Add(user);

        if (roleName is not null)
        {
            var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = roleName, IsSystemDefault = true };
            dbContext.Set<Role>().Add(role);
            dbContext.Set<UserRole>().Add(new UserRole
            {
                Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, OrganizationId = organizationId,
                BranchScope = BranchScope.AllBranches, AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = user.Id
            });
        }
        for (var i = 0; i < branchGrantCount; i++)
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(), UserId = user.Id, BranchId = Guid.NewGuid(), OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
            });
        }
        await dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ListUsersAsync_IncludesRoleNameAndBranchCount()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = await SeedUserAsync(dbContext, organizationId, "BranchManager", branchGrantCount: 2);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(null, null);

        var item = Assert.Single(result.Items);
        Assert.Equal(user.Id, item.Id);
        Assert.Equal("BranchManager", item.RoleName);
        Assert.Equal(2, item.BranchCount);
    }

    [Fact]
    public async Task ListUsersAsync_NoRole_RoleNameIsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedUserAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(null, null);

        Assert.Null(Assert.Single(result.Items).RoleName);
    }

    [Fact]
    public async Task ListUsersAsync_Paginates()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        for (var i = 0; i < 5; i++) await SeedUserAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(1, 2);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task ListUsersAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedUserAsync(dbContext, organizationId);
        await SeedUserAsync(dbContext, foreignOrganizationId);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(null, null);

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }
}
