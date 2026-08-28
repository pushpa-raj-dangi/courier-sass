using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Manifests.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class BranchDashboardService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly IManifestReportingLookupService _manifestLookup;
    private readonly IRiderReportingLookupService _riderLookup;
    private readonly INdrReportingLookupService _ndrLookup;
    private readonly ICodReportingLookupService _codLookup;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public BranchDashboardService(
        IShipmentReportingLookupService shipmentLookup,
        IManifestReportingLookupService manifestLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        ICodReportingLookupService codLookup,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _manifestLookup = manifestLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _codLookup = codLookup;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<BranchDashboardResponse> GetAsync(Guid branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        // Matches ManifestService/NdrService/RiderService/CodService's established
        // CheckBranchAccessAsync convention exactly: 400 REPORTS_VALIDATION_FAILED, not 403.
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "report.read", cancellationToken);
        if (accessibleBranchIds is not null && !accessibleBranchIds.Contains(branchId))
        {
            throw new ReportsValidationException("Branch is outside your branch access for permission 'report.read'.");
        }

        var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);
        var singleBranch = new HashSet<Guid> { branchId };

        var shipmentCountsTask = _shipmentLookup.GetBranchShipmentCountsAsync(branchId, cancellationToken);
        var manifestCountsTask = _manifestLookup.GetBranchManifestCountsAsync(branchId, cancellationToken);
        var missingExceptionsTask = _manifestLookup.GetOpenMissingShipmentExceptionCountAsync(branchId, cancellationToken);
        var riderLoadTask = _riderLookup.GetBranchRiderTaskLoadAsync(branchId, cancellationToken);
        var deliveriesTask = _riderLookup.GetSuccessRateByBranchAsync(singleBranch, todayStart, todayEnd, cancellationToken);
        var codPendingTask = _codLookup.GetBranchPendingFinanceVerificationTotalAsync(branchId, cancellationToken);
        var openNdrTask = _ndrLookup.GetOpenNdrCountForBranchAsync(branchId, cancellationToken);

        await Task.WhenAll(shipmentCountsTask, manifestCountsTask, missingExceptionsTask, riderLoadTask, deliveriesTask, codPendingTask, openNdrTask);

        deliveriesTask.Result.TryGetValue(branchId, out var todaysDeliveries);

        return new BranchDashboardResponse(
            shipmentCountsTask.Result,
            manifestCountsTask.Result,
            missingExceptionsTask.Result,
            riderLoadTask.Result,
            todaysDeliveries ?? new DeliverySuccessStats(0, 0),
            codPendingTask.Result,
            openNdrTask.Result);
    }
}
