using System.Security.Claims;
using Courier.Ndr.Domain;

namespace Courier.Ndr.Application;

// Called exactly once this phase: Riders.Application.RiderService.RecordFailedAttemptAsync, right
// after Shipments.Application.IShipmentTransitionService.FailDeliveryAsync, in the same request —
// the narrow cross-module-write interface pattern established by IUserProvisioningService (Phase
// 8) and IShipmentTransitionService (Phase 7) itself.
public interface INdrCreationService
{
    Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
