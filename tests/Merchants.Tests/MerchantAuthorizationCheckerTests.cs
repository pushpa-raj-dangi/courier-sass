using System.Security.Claims;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantAuthorizationCheckerTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal PrincipalFor(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static MerchantUser NewMerchantUser(Guid id, MerchantUserRole role, bool isActive = true) => new()
    {
        Id = id,
        MerchantId = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Email = $"{id:N}@example.com",
        PasswordHash = "not-used-in-this-test",
        FullName = "Test Merchant User",
        Role = role,
        IsActive = isActive
    };

    [Fact]
    public async Task HasPermissionAsync_OwnerRole_HoldsStaffManage()
    {
        await using var dbContext = CreateContext();
        var ownerId = Guid.NewGuid();
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(ownerId), "merchant.staff.manage");

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_StaffRole_LacksStaffManage()
    {
        await using var dbContext = CreateContext();
        var staffId = Guid.NewGuid();
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(staffId, MerchantUserRole.Staff));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(staffId), "merchant.staff.manage");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_InactiveMerchantUser_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var ownerId = Guid.NewGuid();
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner, isActive: false));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(ownerId), "merchant.profile.read");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UnknownUser_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(Guid.NewGuid()), "merchant.profile.read");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UnauthenticatedPrincipal_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await checker.HasPermissionAsync(anonymous, "merchant.profile.read");

        Assert.False(result);
    }
}
