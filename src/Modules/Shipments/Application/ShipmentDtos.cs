using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record CreateShipmentItemRequest(string Description, int Quantity, decimal UnitValue);

public record CreateShipmentRequest(
    Guid? MerchantId, // ignored/forced to caller's own id for merchant-JWT callers; required for staff callers
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid ServiceTypeId,
    decimal WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal CodAmount,
    bool WantsInsurance,
    string RecipientName,
    string RecipientPhone,
    Guid RecipientProvinceId,
    Guid RecipientDistrictId,
    Guid RecipientLocalLevelId,
    int RecipientWardNumber,
    string RecipientAddressLine,
    string SenderName,
    string SenderPhone,
    Guid SenderProvinceId,
    Guid SenderDistrictId,
    Guid SenderLocalLevelId,
    int SenderWardNumber,
    string SenderAddressLine,
    List<CreateShipmentItemRequest> Items);

public record ShipmentItemResponse(Guid Id, string Description, int Quantity, decimal UnitValue);

public record ShipmentResponse(
    Guid Id,
    string Awb,
    Guid MerchantId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentBranchId,
    Guid? CurrentCustodianUserId,
    Guid? CurrentCustodianRiderId,
    ShipmentStatus CurrentStatus,
    Guid ServiceTypeId,
    decimal WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal DeclaredValue,
    decimal CodAmount,
    bool WantsInsurance,
    decimal DeliveryCharge,
    decimal InsuranceCharge,
    decimal OtherCharges,
    decimal TotalCharge,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddressLine,
    string SenderName,
    string SenderPhone,
    string SenderAddressLine,
    List<ShipmentItemResponse> Items,
    DateTimeOffset CreatedAt);

public class ShipmentValidationException : Exception
{
    public ShipmentValidationException(string message) : base(message) { }
}

// Thrown specifically for an invalid state transition — carries the exact fields
// API-SPEC.md documents in its SHIPMENT_INVALID_TRANSITION error shape ({shipmentId, from, to}),
// so the Presentation layer can map it to that precise response without re-deriving anything.
public class ShipmentInvalidTransitionException : ShipmentValidationException
{
    public ShipmentInvalidTransitionException(Guid shipmentId, ShipmentStatus from, ShipmentStatus to)
        : base($"Cannot transition shipment from {from} to {to}")
    {
        ShipmentId = shipmentId;
        From = from;
        To = to;
    }

    public Guid ShipmentId { get; }
    public ShipmentStatus From { get; }
    public ShipmentStatus To { get; }
}
