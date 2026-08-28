using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class OrgDashboardService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly ICodReportingLookupService _codLookup;
    private readonly IRiderReportingLookupService _riderLookup;
    private readonly INdrReportingLookupService _ndrLookup;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public OrgDashboardService(
        IShipmentReportingLookupService shipmentLookup,
        ICodReportingLookupService codLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _codLookup = codLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<OrgDashboardResponse> GetAsync(DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ReportsDateRange.Resolve(from, to);
        var branchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "report.read", cancellationToken);

        var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        var todaysShipmentsTask = _shipmentLookup.GetCohortStatusCountsAsync(branchIds, todayStart, todayEnd, cancellationToken);
        var financialTask = _codLookup.GetOrgFinancialSummaryAsync(branchIds, cancellationToken);
        var successByBranchTask = _riderLookup.GetSuccessRateByBranchAsync(branchIds, rangeFrom, rangeTo, cancellationToken);
        var successByRiderTask = _riderLookup.GetSuccessRateByRiderAsync(branchIds, rangeFrom, rangeTo, cancellationToken);
        var avgDeliveryTimeTask = _shipmentLookup.GetAverageDeliveryTimeAsync(branchIds, rangeFrom, rangeTo, cancellationToken);
        var shipmentIdsByBranchTask = _shipmentLookup.GetShipmentIdsByDestinationBranchAsync(branchIds, rangeFrom, rangeTo, cancellationToken);

        await Task.WhenAll(todaysShipmentsTask, financialTask, successByBranchTask, successByRiderTask, avgDeliveryTimeTask, shipmentIdsByBranchTask);

        var shipmentIdsByBranch = shipmentIdsByBranchTask.Result;
        var allShipmentIds = shipmentIdsByBranch.Values.SelectMany(ids => ids).ToHashSet();
        var ndrShipmentIds = await _ndrLookup.GetShipmentIdsWithAnyNdrAsync(allShipmentIds, cancellationToken);
        var rtoShipmentIds = await _ndrLookup.GetShipmentIdsWithRtoAsync(allShipmentIds, cancellationToken);

        var successByBranch = successByBranchTask.Result;
        var branchPerformance = new Dictionary<Guid, BranchPerformance>();
        foreach (var (branchId, shipmentIds) in shipmentIdsByBranch)
        {
            var volume = shipmentIds.Count;
            var ndrCount = shipmentIds.Count(id => ndrShipmentIds.Contains(id));
            var rtoCount = shipmentIds.Count(id => rtoShipmentIds.Contains(id));
            successByBranch.TryGetValue(branchId, out var successStats);
            var attempts = successStats?.Attempts ?? 0;
            var successes = successStats?.Successes ?? 0;

            branchPerformance[branchId] = new BranchPerformance(
                volume, attempts, successes,
                attempts > 0 ? (double)successes / attempts : null,
                ndrCount, volume > 0 ? (double)ndrCount / volume : null,
                rtoCount, volume > 0 ? (double)rtoCount / volume : null);
        }

        var riderPerformance = successByRiderTask.Result
            .Select(kv => new RiderPerformance(kv.Key, kv.Value.Attempts, kv.Value.Successes, kv.Value.Attempts > 0 ? (double)kv.Value.Successes / kv.Value.Attempts : null))
            .ToList();

        var totalAttempts = successByBranch.Values.Sum(s => s.Attempts);
        var totalSuccesses = successByBranch.Values.Sum(s => s.Successes);
        var totalShipments = allShipmentIds.Count;
        var totalNdr = ndrShipmentIds.Count(id => allShipmentIds.Contains(id));
        var totalRto = rtoShipmentIds.Count(id => allShipmentIds.Contains(id));

        return new OrgDashboardResponse(
            todaysShipmentsTask.Result,
            financialTask.Result,
            branchPerformance,
            riderPerformance,
            totalAttempts > 0 ? (double)totalSuccesses / totalAttempts : null,
            totalShipments > 0 ? (double)totalNdr / totalShipments : null,
            totalShipments > 0 ? (double)totalRto / totalShipments : null,
            avgDeliveryTimeTask.Result);
    }
}
