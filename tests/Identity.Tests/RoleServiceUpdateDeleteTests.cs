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

public class RoleServiceUpdateDeleteTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
            modelBuilder.ApplyConfiguration(new PermissionConfiguration());
            modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
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

    private static async Task<Role> SeedRoleAsync(TestDbContext dbContext, Guid organizationId, bool isSystemDefault, params string[] permissionKeys)
    {
        var permissions = await dbContext.Set<Permission>().Where(p => permissionKeys.Contains(p.Key)).ToListAsync();
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "TestRole", IsSystemDefault = isSystemDefault };
        foreach (var permission in permissions) role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();
        return role;
    }

    [Fact]
    public async Task UpdateRoleAsync_ReplacesPermissionKeysWholesale()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, false, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        var result = await service.UpdateRoleAsync(role.Id, new UpdateRoleRequest(new[] { "cod.read", "cod.reconcile" }), Guid.NewGuid());

        Assert.Equal(new[] { "cod.read", "cod.reconcile" }, result.PermissionKeys.OrderBy(k => k));
        var persisted = await dbContext.Set<Role>().Include(r => r.Permissions).SingleAsync(r => r.Id == role.Id);
        Assert.DoesNotContain(persisted.Permissions, p => p.Key == "shipment.read");
    }

    [Fact]
    public async Task UpdateRoleAsync_SystemDefaultRole_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, isSystemDefault: true, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<RoleValidationException>(() =>
            service.UpdateRoleAsync(role.Id, new UpdateRoleRequest(new[] { "cod.read" }), Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteRoleAsync_CustomUnassignedRole_Removes()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, false, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        await service.DeleteRoleAsync(role.Id, Guid.NewGuid());

        Assert.False(await dbContext.Set<Role>().AnyAsync(r => r.Id == role.Id));
    }

    [Fact]
    public async Task DeleteRoleAsync_SystemDefaultRole_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, isSystemDefault: true, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<RoleValidationException>(() => service.DeleteRoleAsync(role.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteRoleAsync_StillAssignedToAUser_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, false, "shipment.read");
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RoleId = role.Id, OrganizationId = organizationId,
            BranchScope = BranchScope.AllBranches, AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = Guid.NewGuid()
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<RoleValidationException>(() => service.DeleteRoleAsync(role.Id, Guid.NewGuid()));
        Assert.True(await dbContext.Set<Role>().AnyAsync(r => r.Id == role.Id));
    }
}
