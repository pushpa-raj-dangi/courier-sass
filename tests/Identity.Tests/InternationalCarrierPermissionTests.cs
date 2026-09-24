using Courier.Identity.Application;
using Xunit;

namespace Courier.Identity.Tests;

public class InternationalCarrierPermissionTests
{
    [Theory]
    [InlineData("international.carrier.manage")]
    [InlineData("international.book")]
    [InlineData("international.tracking.manage")]
    public void Catalog_ContainsCarrierPermission(string key)
    {
        Assert.Contains(PermissionCatalog.All, p => p.Key == key);
    }

    [Fact]
    public void OperationsManager_HoldsEveryCarrierOperationPermission()
    {
        var granted = AdditionalRoleSeeds.ByRoleName["OperationsManager"];
        Assert.Contains("international.handoff", granted);
        Assert.Contains("international.receive", granted);
        Assert.Contains("international.book", granted);
        Assert.Contains("international.tracking.manage", granted);
        Assert.DoesNotContain("international.carrier.manage", granted);
    }

    [Fact]
    public void HubStaff_CanBookAndScanButNotOverrideOrManageAccounts()
    {
        var granted = AdditionalRoleSeeds.ByRoleName["HubStaff"];
        Assert.Contains("international.handoff", granted);
        Assert.Contains("international.receive", granted);
        Assert.Contains("international.book", granted);
        Assert.DoesNotContain("international.tracking.manage", granted);
        Assert.DoesNotContain("international.carrier.manage", granted);
    }

    [Fact]
    public void OrganizationAdmin_CanManageCarrierAccounts()
    {
        Assert.Contains("international.carrier.manage", AdditionalRoleSeeds.ByRoleName["OrganizationAdmin"]);
    }

    [Fact]
    public void EverySeededPermission_ExistsInTheCatalog()
    {
        var catalog = PermissionCatalog.All.Select(p => p.Key).ToHashSet();
        foreach (var (role, keys) in AdditionalRoleSeeds.ByRoleName)
        {
            foreach (var key in keys)
            {
                Assert.True(catalog.Contains(key), $"{role} is granted '{key}', which is not in PermissionCatalog.");
            }
        }
    }
}
