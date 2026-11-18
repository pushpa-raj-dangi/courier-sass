using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record CreatePickupRequestRequest(Guid[] ShipmentIds, DateOnly RequestedForDate, string? Notes);

// CodAmount is null when the parcel is prepaid (Shipment.CodAmount == 0).
public record PickupShipmentSummary(Guid Id, string Awb, string? DestinationArea, decimal? CodAmount);

public record PickupRequestResponse(
    Guid Id, Guid MerchantId, Guid BranchId, Guid[] ShipmentIds, DateOnly RequestedForDate, string? Notes,
    PickupRequestStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
    PickupStage Stage, string? BranchName, IReadOnlyList<PickupShipmentSummary> Shipments);
