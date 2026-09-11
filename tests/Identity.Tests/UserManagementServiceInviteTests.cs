using System.Text.Json;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Events.Application;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
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

// Only recognizes an explicit allowlist — mirrors IBranchLookupService's real contract of
// returning null for a branch outside the caller's tenant, which InviteUserAsync now relies on
// to reject a foreign-org BranchId.
file class SelectiveBranchLookupService : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    private readonly HashSet<Guid> _knownBranchIds;
    public SelectiveBranchLookupService(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

public class UserManagementServiceInviteTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => ur.Id);
            modelBuilder.Entity<UserBranch>().HasKey(ub => ub.Id);
            modelBuilder.ApplyConfiguration(new UserInviteConfiguration());
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "https://app.test" }).Build();

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) =>
        CreateService(dbContext, organizationId, new StubBranchLookupService());

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId, IBranchLookupService branchLookupService) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookupService,
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), CreateConfiguration());

    private static async Task<(Guid OrganizationId, Guid RoleId)> SeedOrgAndRoleAsync(TestDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Acme Courier", Slug = $"acme-{organizationId:N}" });
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "BranchManager", IsSystemDefault = true };
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();
        return (organizationId, role.Id);
    }

    [Fact]
    public async Task InviteUserAsync_CreatesUserRoleBranchGrantAndInviteRow()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);

        var result = await service.InviteUserAsync(
            new InviteUserRequest("invitee@example.com", "New Person", null, roleId, branchId, null), Guid.NewGuid());

        var user = await dbContext.Set<User>().SingleAsync(u => u.Id == result.UserId);
        Assert.True(user.IsActive);
        Assert.False(new PasswordHasherAdapter().Verify("anything", user.PasswordHash)); // unusable placeholder hash
        Assert.True(await dbContext.Set<UserRole>().AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == roleId));
        Assert.True(await dbContext.Set<UserBranch>().AnyAsync(ub => ub.UserId == user.Id && ub.BranchId == branchId));
        Assert.True(await dbContext.Set<UserInvite>().AnyAsync(i => i.UserId == user.Id && i.AcceptedAt == null));
    }

    [Fact]
    public async Task InviteUserAsync_WritesOutboxEventWithRecipientEmailAndInviteUrl()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);

        await service.InviteUserAsync(new InviteUserRequest("invitee@example.com", "New Person", null, roleId, Guid.NewGuid(), null), Guid.NewGuid());

        var message = await dbContext.Set<OutboxMessage>().SingleAsync(m => m.EventType == "user.invited");
        var payload = JsonDocument.Parse(message.Payload).RootElement;
        Assert.Equal("invitee@example.com", payload.GetProperty("RecipientEmail").GetString());
        Assert.Equal("Acme Courier", payload.GetProperty("OrganizationName").GetString());
        Assert.StartsWith("https://app.test/accept-invite?token=", payload.GetProperty("InviteUrl").GetString());
    }

    [Fact]
    public async Task InviteUserAsync_DuplicateEmail_Throws()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);
        await service.InviteUserAsync(new InviteUserRequest("dup@example.com", "First", null, roleId, Guid.NewGuid(), null), Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.InviteUserAsync(new InviteUserRequest("dup@example.com", "Second", null, roleId, Guid.NewGuid(), null), Guid.NewGuid()));
    }

    [Fact]
    public async Task InviteUserAsync_UnknownRoleId_Throws()
    {
        await using var dbContext = CreateContext();
        var (organizationId, _) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.InviteUserAsync(new InviteUserRequest("x@example.com", "X", null, Guid.NewGuid(), Guid.NewGuid(), null), Guid.NewGuid()));
    }

    [Fact]
    public async Task InviteUserAsync_RoleBelongsToAnotherOrganization_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, foreignRoleId) = await SeedOrgAndRoleAsync(dbContext);
        var (organizationId, _) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.InviteUserAsync(new InviteUserRequest("x@example.com", "X", null, foreignRoleId, Guid.NewGuid(), null), Guid.NewGuid()));
    }

    [Fact]
    public async Task InviteUserAsync_BranchBelongsToAnotherOrganization_Throws_AndDoesNotCreateAUser()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var foreignBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, new SelectiveBranchLookupService()); // no known branches

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.InviteUserAsync(new InviteUserRequest("x@example.com", "X", null, roleId, foreignBranchId, null), Guid.NewGuid()));
        Assert.False(await dbContext.Set<User>().AnyAsync(u => u.Email == "x@example.com"));
    }
}
