using System.Security.Claims;
using Courier.Merchants.Application;
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

    private static ClaimsPrincipal ApiKeyPrincipal(Guid merchantId, params string[] scopes) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("merchant_id", merchantId.ToString()),
        new Claim("api_key_scopes", string.Join(' ', scopes))
    }, "test"));

    private static MerchantUser NewMerchantUser(Guid id, MerchantUserRole role, bool isActive = true, Guid? merchantId = null) => new()
    {
        Id = id,
        MerchantId = merchantId ?? Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Email = $"{id:N}@example.com",
        PasswordHash = "not-used-in-this-test",
        FullName = "Test Merchant User",
        Role = role,
        IsActive = isActive
    };

    private static Merchant NewMerchant(Guid id, bool isActive = true) => new()
    {
        Id = id,
        OrganizationId = Guid.NewGuid(),
        LegalName = "Test Merchant Legal Name",
        DisplayName = "Test Merchant",
        IsActive = isActive
    };

    [Fact]
    public async Task HasPermissionAsync_OwnerRole_HoldsStaffManage()
    {
        await using var dbContext = CreateContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId));
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner, merchantId: merchantId));
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
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId));
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(staffId, MerchantUserRole.Staff, merchantId: merchantId));
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
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId));
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner, isActive: false, merchantId: merchantId));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(ownerId), "merchant.profile.read");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_ActiveMerchantUserUnderInactiveMerchant_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var ownerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId, isActive: false));
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner, isActive: true, merchantId: merchantId));
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

    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalWithScope_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal(merchantId, "shipment.create", "shipment.read"), "shipment.read");

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalWithoutScope_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal(merchantId, "shipment.read"), MerchantPermissionCatalog.ApiKeysManage);

        Assert.False(result);
    }

    // An API-key credential must be subject to the same Merchant.IsActive invariant the
    // MerchantUser path enforces — otherwise a future merchant-deactivation feature would silently
    // leave every issued API key fully working.
    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalUnderInactiveMerchant_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId, isActive: false));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal(merchantId, "shipment.create", "shipment.read"), "shipment.read");

        Assert.False(result);
    }

    // Control for the case above: identical principal and scope, merchant active — still true.
    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalUnderActiveMerchant_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        dbContext.Set<Merchant>().Add(NewMerchant(merchantId, isActive: true));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal(merchantId, "shipment.create", "shipment.read"), "shipment.read");

        Assert.True(result);
    }

    // A key whose merchant row no longer exists at all is likewise denied.
    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalWithUnknownMerchant_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal(Guid.NewGuid(), "shipment.read"), "shipment.read");

        Assert.False(result);
    }
}
