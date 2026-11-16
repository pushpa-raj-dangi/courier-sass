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

    // Same Join-to-Shipment shape GetOpenNdrCountForBranchAsync already uses -- pushed into SQL,
    // no per-row IShipmentTransitionService lookup needed (NDR is a status-count, not a full
    // response DTO), and no new constructor dependency.
    public async Task<IReadOnlyDictionary<NdrStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var joined = _dbContext.Set<NDR>().AsNoTracking()
            .Join(_dbContext.Set<Shipment>().AsNoTracking(),
                n => n.ShipmentId, s => s.Id, (n, s) => new { n.Status, s.DestinationBranchId });

        var query = joined.AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.DestinationBranchId));
        }

        var counts = await query
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetOpenNdrCountsByBranchAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var destinations = _dbContext.Set<NDR>().AsNoTracking()
            .Where(n => n.Status != NdrStatus.Resolved)
            .Join(_dbContext.Set<Shipment>().AsNoTracking(), n => n.ShipmentId, s => s.Id, (n, s) => s.DestinationBranchId);
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            destinations = destinations.Where(b => branchIdArray.Contains(b));
        }

        var counts = await destinations
            .GroupBy(b => b)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(x => x.BranchId, x => x.Count);
    }
}
