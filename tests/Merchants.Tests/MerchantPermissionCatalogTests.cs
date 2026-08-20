using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantPermissionCatalogTests
{
    [Fact]
    public void Owner_HoldsAllFourPermissions()
    {
        var ownerPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner];

        Assert.Equal(7, ownerPermissions.Count);
        Assert.Contains(MerchantPermissionCatalog.ProfileRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ProfileUpdate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.StaffManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, ownerPermissions);
    }

    [Fact]
    public void Staff_LacksStaffManageAndProfileUpdate()
    {
        var staffPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Staff];

        Assert.Contains(MerchantPermissionCatalog.ProfileRead, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.StaffManage, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.ProfileUpdate, staffPermissions);
    }

    [Theory]
    [InlineData(MerchantUserRole.Owner)]
    [InlineData(MerchantUserRole.Staff)]
    public void RolePermissions_BothRoles_IncludeAllThreeShipmentPermissions(MerchantUserRole role)
    {
        var permissions = MerchantPermissionCatalog.RolePermissions[role];

        Assert.Contains(MerchantPermissionCatalog.ShipmentCreate, permissions);
        Assert.Contains(MerchantPermissionCatalog.ShipmentRead, permissions);
        Assert.Contains(MerchantPermissionCatalog.ShipmentCancel, permissions);
    }

    [Fact]
    public void ShipmentPermissionConstants_MatchStaffPermissionKeys()
    {
        // Deliberately the same string values as the staff PermissionCatalog's
        // shipment.create/read/cancel — two independent catalogs/checkers, same key names,
        // by design (see this plan's Global Constraints).
        Assert.Equal("shipment.create", MerchantPermissionCatalog.ShipmentCreate);
        Assert.Equal("shipment.read", MerchantPermissionCatalog.ShipmentRead);
        Assert.Equal("shipment.cancel", MerchantPermissionCatalog.ShipmentCancel);
    }
}
