using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService : IShipmentItemLookupService
{
    // ShipmentItem has no OrganizationId/tenant query filter of its own — joining through Shipment
    // makes Shipment's tenant filter apply, so a cross-tenant shipmentId yields no items.
    public async Task<IReadOnlyList<ShipmentItemRecord>> GetItemsAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        await (from item in _dbContext.Set<ShipmentItem>()
               join shipment in _dbContext.Set<Shipment>() on item.ShipmentId equals shipment.Id
               where shipment.Id == shipmentId
               select new ShipmentItemRecord(item.Id, item.Description, item.Quantity, item.UnitValue))
            .ToListAsync(cancellationToken);
}
