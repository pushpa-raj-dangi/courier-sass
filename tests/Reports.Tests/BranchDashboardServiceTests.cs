using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Reports.Application;
using Courier.Riders.Application;

namespace Courier.Reports.Tests;

public class BranchDashboardServiceTests
{
    private static ClaimsPrincipal AnyUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task GetAsync_AccessibleBranch_ReturnsComposedResponse()
    {
        var branchId = Guid.NewGuid();
        var shipmentLookup = new FakeShipmentReportingLookupService { BranchShipmentCounts = new Courier.Shipments.Application.BranchShipmentCounts(5, 2, 3) };
        var manifestLookup = new FakeManifestReportingLookupService { ManifestCounts = new BranchManifestCounts(1, 2), MissingShipmentExceptionCount = 1 };
        var riderLookup = new FakeRiderReportingLookupService
        {
            RiderTaskLoad = new List<RiderTaskLoad> { new(Guid.NewGuid(), 3) },
            SuccessRateByBranch = new Dictionary<Guid, DeliverySuccessStats> { [branchId] = new(4, 3) }
        };
        var ndrLookup = new FakeNdrReportingLookupService { OpenNdrCountForBranch = 2 };
        var codLookup = new FakeCodReportingLookupService { BranchPendingFinanceVerificationTotal = 750m };
        var branchLookup = new FakeBranchLookupService { ZoneInfo = new BranchZoneInfo(branchId, null) };
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = new HashSet<Guid> { branchId } };

        var service = new BranchDashboardService(shipmentLookup, manifestLookup, riderLookup, ndrLookup, codLookup, branchLookup, branchChecker);
        var result = await service.GetAsync(branchId, AnyUser());

        Assert.Equal(5, result.ShipmentCounts.Outgoing);
        Assert.Equal(1, result.MissingShipmentExceptions);
        Assert.Single(result.Riders);
        Assert.Equal(4, result.TodaysDeliveries.Attempts);
        Assert.Equal(750m, result.CodPendingFinanceVerification);
        Assert.Equal(2, result.OpenNdrCount);
    }

    [Fact]
    public async Task GetAsync_InaccessibleBranch_ThrowsReportsValidationException()
    {
        var branchId = Guid.NewGuid();
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = new HashSet<Guid> { Guid.NewGuid() } };
        var service = new BranchDashboardService(
            new FakeShipmentReportingLookupService(), new FakeManifestReportingLookupService(), new FakeRiderReportingLookupService(),
            new FakeNdrReportingLookupService(), new FakeCodReportingLookupService(), new FakeBranchLookupService(), branchChecker);

        await Assert.ThrowsAsync<ReportsValidationException>(() => service.GetAsync(branchId, AnyUser()));
    }

    [Fact]
    public async Task GetAsync_AllBranchesScopeButBranchOutsideTenant_ThrowsReportsValidationException()
    {
        // accessibleBranchIds == null ("AllBranches" scope, e.g. an OrganizationOwner) must not
        // be treated as "any branchId is fine" — a branchId belonging to a different organization
        // must still be rejected. FakeBranchLookupService.ZoneInfo defaults to null, mirroring
        // IBranchLookupService.GetZoneInfoAsync's contract for a branch outside the caller's tenant.
        var branchId = Guid.NewGuid();
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = null };
        var service = new BranchDashboardService(
            new FakeShipmentReportingLookupService(), new FakeManifestReportingLookupService(), new FakeRiderReportingLookupService(),
            new FakeNdrReportingLookupService(), new FakeCodReportingLookupService(), new FakeBranchLookupService(), branchChecker);

        await Assert.ThrowsAsync<ReportsValidationException>(() => service.GetAsync(branchId, AnyUser()));
    }
}
