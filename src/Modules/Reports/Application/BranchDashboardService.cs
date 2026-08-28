using System.Security.Claims;
using Courier.Branches.Application;
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
    private readonly IBranchLookupService _branchLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public BranchDashboardService(
        IShipmentReportingLookupService shipmentLookup,
        IManifestReportingLookupService manifestLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        ICodReportingLookupService codLookup,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _manifestLookup = manifestLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _codLookup = codLookup;
        _branchLookupService = branchLookupService;
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

        // accessibleBranchIds == null means "AllBranches" scope, which by itself does not confirm
        // branchId belongs to the caller's own organization — unlike CheckBranchAccessAsync's use
        // elsewhere (Manifest/Ndr/Rider/Shipment services), branchId here comes straight from the
        // route, not from a domain entity already scoped to the caller's tenant. GetZoneInfoAsync
        // returns null both when the branch doesn't exist and when it exists in a different
        // tenant (Branch's ambient OrganizationId query filter excludes it either way), so this
        // also rejects a syntactically valid but cross-tenant branchId.
        if (await _branchLookupService.GetZoneInfoAsync(branchId, cancellationToken) is null)
        {
            throw new ReportsValidationException("Branch is outside your branch access for permission 'report.read'.");
        }

        var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);
        var singleBranch = new HashSet<Guid> { branchId };

        // These lookups all resolve to the same scoped AppDbContext instance (EF Core's DbContext
        // is not safe for concurrent use), so they must run sequentially rather than via
        // Task.WhenAll — running them in parallel throws "A second operation was started on this
        // context instance before a previous operation completed" under a real DbContext (only
        // surfaced once this service is exercised end-to-end against real Postgres in Task 9's
        // Api.Tests; the fakes used by this module's own unit tests never caught it).
        var shipmentCounts = await _shipmentLookup.GetBranchShipmentCountsAsync(branchId, cancellationToken);
        var manifestCounts = await _manifestLookup.GetBranchManifestCountsAsync(branchId, cancellationToken);
        var missingExceptions = await _manifestLookup.GetOpenMissingShipmentExceptionCountAsync(branchId, cancellationToken);
        var riderLoad = await _riderLookup.GetBranchRiderTaskLoadAsync(branchId, cancellationToken);
        var deliveries = await _riderLookup.GetSuccessRateByBranchAsync(singleBranch, todayStart, todayEnd, cancellationToken);
        var codPending = await _codLookup.GetBranchPendingFinanceVerificationTotalAsync(branchId, cancellationToken);
        var openNdr = await _ndrLookup.GetOpenNdrCountForBranchAsync(branchId, cancellationToken);

        deliveries.TryGetValue(branchId, out var todaysDeliveries);

        return new BranchDashboardResponse(
            shipmentCounts,
            manifestCounts,
            missingExceptions,
            riderLoad,
            todaysDeliveries ?? new DeliverySuccessStats(0, 0),
            codPending,
            openNdr);
    }
}
