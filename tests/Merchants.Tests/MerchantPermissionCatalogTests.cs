using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantPermissionCatalogTests
{
    [Fact]
    public void Owner_HoldsAllTwelvePermissions()
    {
        var ownerPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner];

        // 12: Phase 12 added ApiKeysManage as Owner-only (same sensitivity tier as
        // StaffManage/WebhooksManage/ProfileUpdate), and three pickup permissions
        // (PickupsCreate/Read/Cancel) shared with Staff.
        Assert.Equal(12, ownerPermissions.Count);
        Assert.Contains(MerchantPermissionCatalog.ProfileRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ProfileUpdate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.StaffManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.WebhooksManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ApiKeysManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsCreate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsCancel, ownerPermissions);
    }

    [Fact]
    public void Staff_LacksStaffManageAndProfileUpdateAndWebhooksManageAndApiKeysManage()
    {
        var staffPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Staff];

        Assert.Contains(MerchantPermissionCatalog.ProfileRead, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsCreate, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsRead, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsCancel, staffPermissions);
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
