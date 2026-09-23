using Microsoft.EntityFrameworkCore;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public partial class ShipmentService : IShipmentTaskLookupService
{
    public async Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        return shipment is null ? null : new ShipmentTaskInfo(
            shipment.Awb, shipment.RecipientName, shipment.RecipientPhone,
            shipment.RecipientAddressLine, shipment.RecipientWardNumber ?? 0, shipment.CodAmount);
    }
}
