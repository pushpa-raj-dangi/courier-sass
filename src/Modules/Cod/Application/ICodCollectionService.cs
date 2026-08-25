using System.Security.Claims;

namespace Courier.Cod.Application;

// Called exactly once this phase: Riders.Application.RiderService.CompleteDeliveryAsync, right
// after the DeliveryProof row is persisted (Task 6) — same narrow cross-module-write pattern as
// ICodCollectionCreationService above.
public interface ICodCollectionService
{
    Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
