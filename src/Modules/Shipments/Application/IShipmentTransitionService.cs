using System.Security.Claims;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

// Read-only projection for the Manifests module's five-check scan validation
// (BRANCH-OPERATIONS.md §1) — deliberately narrower than ShipmentResponse: only the fields a scan
// check needs (state + custody), not pricing/recipient/sender data Manifests has no reason to see.
// CurrentCustodianRiderId (Phase 8) lets Riders perform its own check 5 (custody consistency) the
// same way Manifests already does with CurrentBranchId — Riders is the only caller that reads it.
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentCustodianRiderId);

// Lets Manifests and Riders drive real shipment-status transitions without touching Shipment's
// DbSet directly. Both callers perform the permission + branch-access + expected-operation +
// custody checks themselves (BRANCH-OPERATIONS.md §1 checks 1/2/4/5) before calling these — the
// methods below only enforce the state-machine check (#3, via the underlying TransitionAsync's
// expectedFrom guard). Phase 9 adds the NDR/RTO transition surface, driven by the new Ndr module
// and by Manifests for the RTO transport leg only.
public interface IShipmentTransitionService
{
    Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // Phase 8 — driven by Riders.Application.RiderService.
    Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // Phase 9 — driven by Ndr.Application (NdrCreationService, NdrService) and Manifests.Application
    // (ManifestService, for the RTO transport leg only).
    Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
