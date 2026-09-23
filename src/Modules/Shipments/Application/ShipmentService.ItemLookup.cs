using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService : IShipmentItemLookupService
{
    public async Task<IReadOnlyList<ShipmentItemRecord>> GetItemsAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ShipmentItem>()
            .Where(i => i.ShipmentId == shipmentId)
            .Select(i => new ShipmentItemRecord(i.Id, i.Description, i.Quantity, i.UnitValue))
            .ToListAsync(cancellationToken);
}
