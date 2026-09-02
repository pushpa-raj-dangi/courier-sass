using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Xunit;

namespace Courier.Identity.Tests;

public class RolesEndpointsGroupingTests
{
    [Fact]
    public void GroupPermissions_GroupsByPrefixBeforeFirstDot_SortedAlphabetically()
    {
        var groups = RolesEndpoints.GroupPermissions(PermissionCatalog.All);

        Assert.Equal(groups.Select(g => g.Group), groups.Select(g => g.Group).OrderBy(g => g, StringComparer.Ordinal));

        var shipmentGroup = groups.Single(g => g.Group == "shipment");
        Assert.Contains(shipmentGroup.Permissions, p => p.Key == "shipment.create");
        Assert.Contains(shipmentGroup.Permissions, p => p.Key == "shipment.transfer");
        Assert.DoesNotContain(shipmentGroup.Permissions, p => p.Key == "delivery.assign");

        // "org.billing.manage" has two dots — the group is the token before the FIRST dot.
        var orgGroup = groups.Single(g => g.Group == "org");
        Assert.Contains(orgGroup.Permissions, p => p.Key == "org.billing.manage");

        var totalPermissions = groups.Sum(g => g.Permissions.Count);
        Assert.Equal(PermissionCatalog.All.Count, totalPermissions);
    }
}
