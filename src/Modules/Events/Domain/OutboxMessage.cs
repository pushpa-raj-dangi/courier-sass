namespace Courier.Events.Domain;

// Written by IOutboxWriter.WriteAsync inside the same SaveChangesAsync as the business change
// that caused it (Global Constraints) — this is what guarantees "state changed, no event" can
// never happen. Payload is a plain JSON string; Events has no project reference to any other
// module's domain types, so it cannot and does not know what shape the payload has.
public class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
}
