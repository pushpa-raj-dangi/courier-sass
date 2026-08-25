using System.Security.Claims;

namespace Courier.Cod.Application;

// Called exactly once this phase: Shipments.Application.ShipmentService.CreateShipmentAsync, right
// after a shipment with CodAmount > 0 is persisted (Task 5) — the narrow cross-module-write
// interface pattern established by IUserProvisioningService (Phase 8) and INdrCreationService
// (Phase 9). expectedAmount/shipmentTotalCharge/merchantId/branchId are snapshotted onto the new
// CODCollection row rather than looked up later (see this plan's Global Constraints).
public interface ICodCollectionCreationService
{
    Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
