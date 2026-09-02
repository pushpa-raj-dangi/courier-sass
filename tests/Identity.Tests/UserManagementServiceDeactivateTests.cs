using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
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
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class UserManagementServiceDeactivateTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(u => u.Id);
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
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService());

    [Fact]
    public async Task DeactivateUserAsync_SetsIsActiveFalse()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = "x@example.com", PasswordHash = "x", FullName = "X", IsActive = true };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        await service.DeactivateUserAsync(user.Id, Guid.NewGuid());

        Assert.False((await dbContext.Set<User>().SingleAsync(u => u.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task ReactivateUserAsync_SetsIsActiveTrue()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = "x@example.com", PasswordHash = "x", FullName = "X", IsActive = false };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        await service.ReactivateUserAsync(user.Id, Guid.NewGuid());

        Assert.True((await dbContext.Set<User>().SingleAsync(u => u.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task DeactivateUserAsync_UnknownUser_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() => service.DeactivateUserAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task DeactivateUserAsync_AnotherOrganizationsUser_Throws_AndDoesNotDeactivate()
    {
        await using var dbContext = CreateContext();
        var foreignOrganizationId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), OrganizationId = foreignOrganizationId, Email = "x@example.com", PasswordHash = "x", FullName = "X", IsActive = true };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var callerFromAnotherOrg = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() => callerFromAnotherOrg.DeactivateUserAsync(user.Id, Guid.NewGuid()));
        Assert.True((await dbContext.Set<User>().SingleAsync(u => u.Id == user.Id)).IsActive);
    }
}
