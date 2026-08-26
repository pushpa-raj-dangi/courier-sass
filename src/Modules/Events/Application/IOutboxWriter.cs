namespace Courier.Events.Application;

// The only public surface of the Events module — every event-producing application service
// (Shipments, Riders, Ndr, Cod, and any future one) calls this exactly once per domain event,
// inside the same SaveChangesAsync as the business change itself (never a separate commit).
// payload is any plain object; it is JSON-serialized as-is. See Global Constraints for the
// "every wired event's payload includes ShipmentId and MerchantId" convention this plan follows,
// which is a caller-side convention, not something IOutboxWriter enforces or knows about.
public interface IOutboxWriter
{
    Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default);
}
