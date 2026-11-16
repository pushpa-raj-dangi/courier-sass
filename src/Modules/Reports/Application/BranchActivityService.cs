using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class BranchActivityService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly IRiderReportingLookupService _riderLookup;
    private readonly INdrReportingLookupService _ndrLookup;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public BranchActivityService(
        IShipmentReportingLookupService shipmentLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<BranchActivityResponse> GetAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var branchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "branch.read", cancellationToken);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        // Sequential awaits: every lookup shares one scoped DbContext (see BranchDashboardService).
        var custody = await _shipmentLookup.GetBranchCustodyCountsAsync(branchIds, cancellationToken);
        var ridersOut = await _riderLookup.GetRidersOutCountsAsync(branchIds, today, cancellationToken);
        var openNdrs = await _ndrLookup.GetOpenNdrCountsByBranchAsync(branchIds, cancellationToken);

        // The lookups already narrow to branchIds, so a scoped caller's rows are exactly its scope.
        var rowBranchIds = branchIds is not null
            ? new HashSet<Guid>(branchIds)
            : new HashSet<Guid>(custody.Keys.Concat(ridersOut.Keys).Concat(openNdrs.Keys));

        var rows = rowBranchIds
            .OrderBy(id => id)
            .Select(id =>
            {
                custody.TryGetValue(id, out var counts);
                return new BranchActivityRow(
                    id, counts?.OnHand ?? 0, counts?.OutForDelivery ?? 0,
                    ridersOut.GetValueOrDefault(id), openNdrs.GetValueOrDefault(id));
            })
            .ToList();
        return new BranchActivityResponse(rows);
    }
}
