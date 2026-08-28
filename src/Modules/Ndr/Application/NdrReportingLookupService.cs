using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public class NdrReportingLookupService : INdrReportingLookupService
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTransitionService _shipmentTransitionService;

    public NdrReportingLookupService(DbContext dbContext, IShipmentTransitionService shipmentTransitionService)
    {
        _dbContext = dbContext;
        _shipmentTransitionService = shipmentTransitionService;
    }

    // Reuses NdrService.ListAsync's established per-row GetScanInfoAsync branch-resolution
    // pattern (Phase 9) — NDR carries no branch column of its own.
    public async Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var openNdrShipmentIds = await _dbContext.Set<NDR>()
            .Where(n => n.Status != NdrStatus.Resolved)
            .Select(n => n.ShipmentId)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var shipmentId in openNdrShipmentIds)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
            if (scanInfo is not null && scanInfo.DestinationBranchId == branchId)
            {
                count++;
            }
        }
        return count;
    }

    public async Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }
        var idArray = shipmentIds.ToArray();
        var matched = await _dbContext.Set<NDR>()
            .Where(n => idArray.Contains(n.ShipmentId))
            .Select(n => n.ShipmentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return matched.ToHashSet();
    }

    public async Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }
        var idArray = shipmentIds.ToArray();
        var matched = await _dbContext.Set<ReturnShipment>()
            .Where(r => idArray.Contains(r.OriginalShipmentId))
            .Select(r => r.OriginalShipmentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return matched.ToHashSet();
    }
}
