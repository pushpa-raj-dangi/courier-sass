using System.Security.Claims;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

// The I3 request shape mirrors CreateShipmentRequest (ShipmentDtos.cs) but: (a) has no CodAmount
// field at all — COD-on-international is enforced by omission, not a runtime check; (b) has no
// WantsInsurance — insurance is a domestic-pricing concept not wired for international shipments
// in this plan; (c) the recipient/sender geography fields are nullable, and international-address
// fields are added, matching Shipment's own Task 1 shape; (d) ProvisionalTotalChargeAmount/
// Currency replace pricing-engine-derived charges (I5 doesn't exist yet).
public record CreateInternationalShipmentShellRequest(
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

// Lets International.Application create and progress a Shipment without ever touching
// Shipment's DbSet directly — same precedent as IShipmentTransitionService (used by
// Manifests/Riders/Ndr). Implemented by ShipmentService (ShipmentService.International.cs).
public interface IShipmentInternationalGateway
{
    Task<ShipmentResponse> CreateShipmentShellAsync(CreateInternationalShipmentShellRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
