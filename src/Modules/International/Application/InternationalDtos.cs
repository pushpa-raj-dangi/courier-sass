using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.International.Application;

public class InternationalShipmentValidationException : Exception
{
    public InternationalShipmentValidationException(string message) : base(message) { }
}

// Same shape as Shipments.Application.CreateInternationalShipmentShellRequest, minus nothing —
// InternationalShipmentService passes every field straight through to the gateway. Kept as its
// own record (not a reuse of CreateInternationalShipmentShellRequest) so the Presentation layer's
// request-binding type lives in International, not Shipments — International owns its own public
// API contract.
public record CreateInternationalShipmentRequest(
    Guid MerchantId,
    ShipmentDirection Direction,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid ServiceTypeId,
    decimal WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    string RecipientName,
    string RecipientPhone,
    Guid? RecipientProvinceId,
    Guid? RecipientDistrictId,
    Guid? RecipientLocalLevelId,
    int? RecipientWardNumber,
    string RecipientAddressLine,
    string? RecipientCity,
    string? RecipientRegion,
    string? RecipientPostalCode,
    string? RecipientCountryCode,
    string SenderName,
    string SenderPhone,
    Guid? SenderProvinceId,
    Guid? SenderDistrictId,
    Guid? SenderLocalLevelId,
    int? SenderWardNumber,
    string SenderAddressLine,
    string? SenderCity,
    string? SenderRegion,
    string? SenderPostalCode,
    string? SenderCountryCode,
    decimal ProvisionalTotalChargeAmount,
    string ProvisionalTotalChargeCurrency,
    List<CreateShipmentItemRequest> Items);

public record CustomsItemRequest(Guid ShipmentItemId, string HsCode, decimal CustomsValueAmount, string CustomsValueCurrency);

public record UpdateCustomsRequest(
    string ContentsDescription,
    string CountryOfOrigin,
    string Purpose,
    string Incoterm,
    string? ExportLicenceReference,
    List<CustomsItemRequest> Items);

public record InternationalLegResponse(
    Guid ShipmentId,
    string ContentsDescription,
    string CountryOfOrigin,
    string Purpose,
    string Incoterm,
    string? ExportLicenceReference,
    bool CustomsCompleted,
    decimal ProvisionalTotalChargeAmount,
    string ProvisionalTotalChargeCurrency,
    bool IsProvisionalPricing,
    IReadOnlyList<CustomsItemResponse> CustomsItems);

public record CustomsItemResponse(Guid ShipmentItemId, string HsCode, decimal CustomsValueAmount, string CustomsValueCurrency);

// I4 adds the carrier booking (live one + history) and the carrier event timeline as trailing
// optionals so every existing (shipment, leg) construction keeps compiling.
public record InternationalShipmentDetailResponse(
    ShipmentResponse Shipment,
    InternationalLegResponse Leg,
    CarrierBookingResponse? Booking = null,
    IReadOnlyList<CarrierBookingResponse>? BookingHistory = null,
    IReadOnlyList<CarrierTrackingEventResponse>? CarrierEvents = null);
