using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record BranchShipmentCounts(int Outgoing, int PendingSorting, int PendingDispatch);

public record ShipmentExportRow(
    string Awb, ShipmentStatus Status, Guid OriginBranchId, Guid DestinationBranchId, Guid MerchantId,
    decimal CodAmount, decimal TotalCharge, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt);

// Consumed by Courier.Reports.Application — no method takes organizationId; every query relies on
// AppDbContext's ambient tenant filter. Shipment carries its own OrganizationId with an ambient query
// filter; ShipmentEvent has neither its own column nor its own filter — every method here that touches
// ShipmentEvent must join/intersect back to a tenant-filtered Shipment query before returning data,
// never query ShipmentEvent standalone.
public interface IShipmentReportingLookupService
{
    Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<TimeSpan?> GetAverageDeliveryTimeAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
}
