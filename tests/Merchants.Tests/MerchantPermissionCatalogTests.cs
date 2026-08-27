using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantPermissionCatalogTests
{
    [Fact]
    public void Owner_HoldsAllNinePermissions()
    {
        var ownerPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner];

        // 9, not 8: Phase 12 added ApiKeysManage as an Owner-only permission (same sensitivity
        // tier as StaffManage/WebhooksManage/ProfileUpdate).
        Assert.Equal(9, ownerPermissions.Count);
        Assert.Contains(MerchantPermissionCatalog.ProfileRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ProfileUpdate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.StaffManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.WebhooksManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ApiKeysManage, ownerPermissions);
    }

    [Fact]
    public void Staff_LacksStaffManageAndProfileUpdateAndWebhooksManageAndApiKeysManage()
    {
        var staffPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Staff];

        Assert.Contains(MerchantPermissionCatalog.ProfileRead, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.StaffManage, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.ProfileUpdate, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.WebhooksManage, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.ApiKeysManage, staffPermissions);
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
