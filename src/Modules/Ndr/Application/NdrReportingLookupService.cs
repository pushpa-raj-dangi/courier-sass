using Courier.Ndr.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public class NdrReportingLookupService : INdrReportingLookupService
{
    private readonly DbContext _dbContext;

    public NdrReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Collapsed from a per-row GetScanInfoAsync N+1 loop into a single join query: NDR carries no
    // branch column of its own, so the destination branch is resolved via a join back to Shipment
    // (Ndr.csproj already references Shipments.csproj). Shipment carries its own OrganizationId with
    // an ambient query filter, so this join is at least as tenant-safe as the original per-row lookup.
    public async Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<NDR>().AsNoTracking()
            .Where(n => n.Status != NdrStatus.Resolved)
            .Join(_dbContext.Set<Shipment>().AsNoTracking(),
                n => n.ShipmentId, s => s.Id, (n, s) => s.DestinationBranchId)
            .CountAsync(b => b == branchId, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }
        var idArray = shipmentIds.ToArray();
        var matched = await _dbContext.Set<NDR>().AsNoTracking()
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
        var matched = await _dbContext.Set<ReturnShipment>().AsNoTracking()
            .Where(r => idArray.Contains(r.OriginalShipmentId))
            .Select(r => r.OriginalShipmentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return matched.ToHashSet();
    }
}
