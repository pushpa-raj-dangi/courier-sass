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

        Assert.Equal(4, ownerPermissions.Count);
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
}
