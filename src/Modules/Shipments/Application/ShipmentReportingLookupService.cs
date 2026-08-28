using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public class ShipmentReportingLookupService : IShipmentReportingLookupService
{
    private readonly DbContext _dbContext;

    public ShipmentReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var createdShipmentIds = _dbContext.Set<ShipmentEvent>().AsNoTracking()
            .Where(e => e.EventType == ShipmentEventType.Created && e.Timestamp >= from && e.Timestamp < to)
            .Select(e => e.ShipmentId);

        var query = _dbContext.Set<Shipment>().AsNoTracking().Where(s => createdShipmentIds.Contains(s.Id));
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(s => branchIdArray.Contains(s.OriginBranchId));
        }

        var counts = await query
            .GroupBy(s => s.CurrentStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }

    public async Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var pendingSorting = await _dbContext.Set<Shipment>().AsNoTracking()
            .CountAsync(s => s.CurrentBranchId == branchId && s.CurrentStatus == ShipmentStatus.AtOriginBranch, cancellationToken);
        var pendingDispatch = await _dbContext.Set<Shipment>().AsNoTracking()
            .CountAsync(s => s.CurrentBranchId == branchId && s.CurrentStatus == ShipmentStatus.Sorted, cancellationToken);
        return new BranchShipmentCounts(pendingSorting + pendingDispatch, pendingSorting, pendingDispatch);
    }

    public async Task<TimeSpan?> GetAverageDeliveryTimeAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var deliveredEvents = _dbContext.Set<ShipmentEvent>().AsNoTracking()
            .Where(e => e.EventType == ShipmentEventType.Delivered && e.Timestamp >= from && e.Timestamp < to);
        var createdEvents = _dbContext.Set<ShipmentEvent>().AsNoTracking().Where(e => e.EventType == ShipmentEventType.Created);

        var query =
            from delivered in deliveredEvents
            join created in createdEvents on delivered.ShipmentId equals created.ShipmentId
            join shipment in _dbContext.Set<Shipment>().AsNoTracking() on delivered.ShipmentId equals shipment.Id
            select new { shipment.OriginBranchId, DeliveredAt = delivered.Timestamp, CreatedAt = created.Timestamp };

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.OriginBranchId));
        }

        var durationTicks = await query.Select(x => (double)(x.DeliveredAt - x.CreatedAt).Ticks).ToListAsync(cancellationToken);
        return durationTicks.Count == 0 ? null : TimeSpan.FromTicks((long)durationTicks.Average());
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var createdShipmentIds = _dbContext.Set<ShipmentEvent>().AsNoTracking()
            .Where(e => e.EventType == ShipmentEventType.Created && e.Timestamp >= from && e.Timestamp < to)
            .Select(e => e.ShipmentId);

        var query = _dbContext.Set<Shipment>().AsNoTracking().Where(s => createdShipmentIds.Contains(s.Id));
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(s => branchIdArray.Contains(s.DestinationBranchId));
        }

        var rows = await query.Select(s => new { s.DestinationBranchId, s.Id }).ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.DestinationBranchId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.Id).ToList());
    }

    public async Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var createdEvents = _dbContext.Set<ShipmentEvent>().AsNoTracking()
            .Where(e => e.EventType == ShipmentEventType.Created && e.Timestamp >= from && e.Timestamp < to);

        var query =
            from created in createdEvents
            join shipment in _dbContext.Set<Shipment>().AsNoTracking() on created.ShipmentId equals shipment.Id
            select new { Shipment = shipment, CreatedAt = created.Timestamp };

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.Shipment.OriginBranchId));
        }

        var rows = await query.ToListAsync(cancellationToken);
        var shipmentIds = rows.Select(r => r.Shipment.Id).ToList();
        var deliveredAtByShipmentId = await _dbContext.Set<ShipmentEvent>().AsNoTracking()
            .Where(e => e.EventType == ShipmentEventType.Delivered && shipmentIds.Contains(e.ShipmentId))
            .ToDictionaryAsync(e => e.ShipmentId, e => e.Timestamp, cancellationToken);

        return rows.Select(r => new ShipmentExportRow(
            r.Shipment.Awb, r.Shipment.CurrentStatus, r.Shipment.OriginBranchId, r.Shipment.DestinationBranchId,
            r.Shipment.MerchantId, r.Shipment.CodAmount, r.Shipment.TotalCharge, r.CreatedAt,
            deliveredAtByShipmentId.TryGetValue(r.Shipment.Id, out var deliveredAt) ? deliveredAt : null)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var idArray = shipmentIds.ToArray();
        var rows = await _dbContext.Set<Shipment>().AsNoTracking()
            .Where(s => idArray.Contains(s.Id))
            .Select(s => new { s.Id, s.Awb })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Awb);
    }
}
