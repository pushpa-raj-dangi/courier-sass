using System.Security.Claims;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

// Read-only projection for the Manifests module's five-check scan validation
// (BRANCH-OPERATIONS.md §1) — deliberately narrower than ShipmentResponse: only the fields a scan
// check needs (state + custody), not pricing/recipient/sender data Manifests has no reason to see.
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId);

// Lets the Manifests module drive real shipment-status transitions without touching Shipment's
// DbSet directly, mirroring Phase 5's IBranchLookupService pattern. Manifests performs the
// permission + branch-access + expected-operation checks itself (BRANCH-OPERATIONS.md §1 checks
// 1/2/4) before calling these — the transition methods below only enforce the state-machine check
// (#3, via the underlying TransitionAsync's expectedFrom guard) and are not separately
// permission-gated, since that gate now lives one layer up in ManifestService.
public interface IShipmentTransitionService
{
    Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
