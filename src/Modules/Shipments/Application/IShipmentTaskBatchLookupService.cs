namespace Courier.Shipments.Application;

// F15 R4 rider "my day": task-card details for many shipments in one query. Kept off
// IShipmentTaskLookupService (single shipment) so its six test fakes stay untouched.
public interface IShipmentTaskBatchLookupService
{
    Task<IReadOnlyDictionary<Guid, ShipmentTaskInfo>> GetTaskInfosAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);
}
