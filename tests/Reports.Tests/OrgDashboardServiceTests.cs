using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Reports.Application;
using Courier.Riders.Application;
using Courier.Shipments.Domain;

namespace Courier.Reports.Tests;

public class OrgDashboardServiceTests
{
    private static ClaimsPrincipal AnyUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task GetAsync_ComputesOverallRatesFromSummedNumeratorsAndDenominators_NotAveragedPerBranchRates()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var shipmentsA = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };   // 2 shipments, branch A
        var shipmentsB = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList(); // 8 shipments, branch B
        var shipmentIdsByBranch = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [branchA] = shipmentsA,
            [branchB] = shipmentsB
        };

        // Branch A: 1/2 NDR (50%). Branch B: 1/8 NDR (12.5%). A naive average would give 31.25%;
        // the correct pooled rate is 2/10 = 20%.
        var ndrShipmentIds = new HashSet<Guid> { shipmentsA[0], shipmentsB[0] };

        var shipmentLookup = new FakeShipmentReportingLookupService
        {
            CohortStatusCounts = new Dictionary<ShipmentStatus, int> { [ShipmentStatus.Created] = 3 },
            ShipmentIdsByDestinationBranch = shipmentIdsByBranch,
            AverageDeliveryTime = TimeSpan.FromHours(4)
        };
        var ndrLookup = new FakeNdrReportingLookupService { ShipmentIdsWithAnyNdr = ndrShipmentIds, ShipmentIdsWithRto = new HashSet<Guid>() };
        var riderLookup = new FakeRiderReportingLookupService
        {
            SuccessRateByBranch = new Dictionary<Guid, DeliverySuccessStats>
            {
                [branchA] = new DeliverySuccessStats(2, 1),
                [branchB] = new DeliverySuccessStats(8, 8)
            },
            SuccessRateByRider = new Dictionary<Guid, DeliverySuccessStats>()
        };
        var codLookup = new FakeCodReportingLookupService { FinancialSummary = new CodFinancialSummary(1000m, 800m, 200m, 600m, 100m) };
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = null };

        var service = new OrgDashboardService(shipmentLookup, codLookup, riderLookup, ndrLookup, branchChecker);
        var result = await service.GetAsync(null, null, AnyUser());

        Assert.Equal(0.2, result.OverallNdrRate);
        Assert.Equal(9.0 / 10.0, result.OverallDeliverySuccessRate);
        Assert.Equal(TimeSpan.FromHours(4), result.AverageDeliveryTime);
        Assert.Equal(1000m, result.Financial.Expected);
    }

    [Fact]
    public async Task GetAsync_BranchWithNoShipments_RateIsNullNotDivisionByZero()
    {
        var branchWithNoVolume = Guid.NewGuid();
        var shipmentLookup = new FakeShipmentReportingLookupService
        {
            ShipmentIdsByDestinationBranch = new Dictionary<Guid, IReadOnlyList<Guid>> { [branchWithNoVolume] = new List<Guid>() }
        };
        var service = new OrgDashboardService(
            shipmentLookup, new FakeCodReportingLookupService(), new FakeRiderReportingLookupService(),
            new FakeNdrReportingLookupService(), new FakeBranchAuthorizationChecker { AccessibleBranchIds = null });

        var result = await service.GetAsync(null, null, AnyUser());

        Assert.Null(result.BranchPerformance[branchWithNoVolume].NdrRate);
        Assert.Null(result.BranchPerformance[branchWithNoVolume].RtoRate);
    }
}
