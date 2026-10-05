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
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastEventAt = null);

// Optional, AND-combined narrowing for GET /api/v1/shipments (F15 R2). Applied AFTER the
// tenant filter and the caller's merchant pin / staff branch scope, so it can only ever narrow.
public record ShipmentListFilter(
    IReadOnlyList<ShipmentStatus>? Statuses,
    string? Query,
    Guid? MerchantId,
    Guid? ServiceTypeId,
    Guid? BranchId,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo)
{
    public bool IsEmpty =>
        (Statuses is null || Statuses.Count == 0) && Query is null && MerchantId is null &&
        ServiceTypeId is null && BranchId is null && CreatedFrom is null && CreatedTo is null;

    public static ShipmentListFilter Parse(
        string[]? status, string? q, Guid? merchantId, Guid? serviceTypeId, Guid? branchId,
        DateTimeOffset? createdFrom, DateTimeOffset? createdTo)
    {
        List<ShipmentStatus>? statuses = null;
        if (status is { Length: > 0 })
        {
            statuses = new List<ShipmentStatus>();
            foreach (var raw in status)
            {
                // Exact enum member names only — Enum.TryParse would otherwise accept numeric
                // strings ("3") and, with ignoreCase, alternate casings the frontend never sends.
                if (string.IsNullOrEmpty(raw) || !raw.All(char.IsLetter) ||
                    !Enum.TryParse<ShipmentStatus>(raw, ignoreCase: false, out var parsed) ||
                    parsed.ToString() != raw)
                {
                    throw new ShipmentValidationException($"Unknown shipment status '{raw}'.");
                }
                statuses.Add(parsed);
            }
        }

        var query = q?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            query = null;
        }
        else if (query.Length < 2)
        {
            throw new ShipmentValidationException("Search must be at least 2 characters.");
        }

        if (createdFrom is not null && createdTo is not null && createdFrom > createdTo)
        {
            throw new ShipmentValidationException("createdFrom must not be after createdTo.");
        }

        return new ShipmentListFilter(statuses, query, merchantId, serviceTypeId, branchId, createdFrom, createdTo);
    }
}

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

// Thrown when a save collides with a concurrent update to the same shipment (an xmin mismatch) —
// not necessarily an invalid transition, just a race the caller lost. Derives from
// ShipmentInvalidTransitionException so existing Presentation-layer mapping is unchanged, but a
// caller that distinguishes retryable races from genuine business-rule violations (e.g.
// CarrierEventApplier) can catch this type specifically, before the base type.
public class ShipmentConcurrencyException : ShipmentInvalidTransitionException
{
    public ShipmentConcurrencyException(Guid shipmentId, ShipmentStatus from, ShipmentStatus to)
        : base(shipmentId, from, to) { }
}
