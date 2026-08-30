namespace Courier.Shipments.Application;

public record ShipmentTaskInfo(
    string Awb, string RecipientName, string RecipientPhone,
    string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);

// Narrow read-only projection for Riders' "my tasks today" view (Phase F5 frontend) — mirrors
// IBranchLookupService's cross-module lookup pattern so Riders never touches Shipments' DbSet
// directly. Deliberately separate from ShipmentScanInfo: that record is for scan-check validation
// (state + custody), this one is for rendering a task card (recipient contact/address + COD).
public interface IShipmentTaskLookupService
{
    Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
}
