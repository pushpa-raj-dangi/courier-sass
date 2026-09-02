using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
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

public class RoleServiceListCreateTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
            modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static RoleService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());

    private static async Task<Role> SeedRoleAsync(TestDbContext dbContext, Guid organizationId, string name, bool isSystemDefault, params string[] permissionKeys)
    {
        var permissions = await dbContext.Set<Permission>().Where(p => permissionKeys.Contains(p.Key)).ToListAsync();
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = name, IsSystemDefault = isSystemDefault };
        foreach (var permission in permissions) role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();
        return role;
    }

    [Fact]
    public async Task ListRolesAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedRoleAsync(dbContext, organizationId, "OwnRole", true, "shipment.read");
        await SeedRoleAsync(dbContext, foreignOrganizationId, "ForeignRole", true, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListRolesAsync();

        var single = Assert.Single(result);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListRolesAsync_IncludesPermissionKeys()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedRoleAsync(dbContext, organizationId, "Dispatcher", true, "manifest.read", "manifest.create");
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListRolesAsync();

        var role = Assert.Single(result);
        Assert.Equal(new[] { "manifest.create", "manifest.read" }, role.PermissionKeys.OrderBy(k => k));
    }

    [Fact]
    public async Task CreateRoleAsync_ValidPermissionKeys_CreatesNonSystemDefaultRole()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);

        var result = await service.CreateRoleAsync(
            new CreateRoleRequest("CustomAuditor", new[] { "audit.read", "shipment.read" }), Guid.NewGuid());

        Assert.False(result.IsSystemDefault);
        Assert.Equal(new[] { "audit.read", "shipment.read" }, result.PermissionKeys.OrderBy(k => k));
        var persisted = await dbContext.Set<Role>().Include(r => r.Permissions).SingleAsync(r => r.Id == result.Id);
        Assert.Equal(organizationId, persisted.OrganizationId);
        Assert.False(persisted.IsSystemDefault);
    }

    [Fact]
    public async Task CreateRoleAsync_UnknownPermissionKey_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<RoleValidationException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest("Bad", new[] { "not.a.real.permission" }), Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateRoleAsync_EmptyName_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<RoleValidationException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest("", new[] { "shipment.read" }), Guid.NewGuid()));
    }
}
