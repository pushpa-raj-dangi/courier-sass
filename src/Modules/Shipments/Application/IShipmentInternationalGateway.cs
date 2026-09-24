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

// Identifies the carrier tracking event that drove a transition; serialized into the
// ShipmentEvent's Metadata so the timeline can show which carrier reported it.
public record CarrierEventReference(Guid CarrierAccountId, string CarrierAwb, string CarrierEventId, string CarrierStatusCode, string? Location);

// Identifies which carrier account/AWB a shipment was physically handed to; serialized into the
// ShipmentEvent's Metadata for HandToCarrierAsync below.
public record CarrierHandoffReference(Guid CarrierAccountId, string CarrierAwb);

public enum CarrierStatusApplyResult
{
    Applied,
    AlreadyAtStatus,
    NotFound
}

// Lets International.Application create and progress a Shipment without ever touching
// Shipment's DbSet directly — same precedent as IShipmentTransitionService (used by
// Manifests/Riders/Ndr). Implemented by ShipmentService (ShipmentService.International.cs).
public interface IShipmentInternationalGateway
{
    Task<ShipmentResponse> CreateShipmentShellAsync(CreateInternationalShipmentShellRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // I4 — carrier-driven (no user). Only InternationalTransitionRules' Carrier moves are
    // allowed; throws ShipmentInvalidTransitionException otherwise, ShipmentValidationException
    // for a non-international shipment.
    Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default);

    // I4 — staff exit from CustomsHold/InternationalException, with a required reason.
    Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // I4 — handoff scan: CustomsPrepared -> HandedToCarrier; our custody ends.
    Task<ShipmentResponse?> HandToCarrierAsync(Guid shipmentId, CarrierHandoffReference reference, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // I4 — receive scan at the inbound gateway (OriginBranchId): -> ReceivedFromCarrier; custody
    // resumes. When the gateway is also the destination, arrives (AtDestinationBranch) in the same save.
    Task<ShipmentResponse?> ReceiveFromCarrierAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
