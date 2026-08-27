using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record CreatePickupRequestRequest(Guid[] ShipmentIds, DateOnly RequestedForDate, string? Notes);

public record PickupRequestResponse(Guid Id, Guid MerchantId, Guid BranchId, Guid[] ShipmentIds, DateOnly RequestedForDate, string? Notes, PickupRequestStatus Status, DateTime CreatedAt, DateTime UpdatedAt);
