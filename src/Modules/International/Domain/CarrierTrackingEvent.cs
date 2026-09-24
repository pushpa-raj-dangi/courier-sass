namespace Courier.International.Domain;

// Append-only record of one carrier-reported tracking event, from any source (I4 spec §4.3).
// Deduplicated by (CarrierAccountId, CarrierEventId). After insert only the processing columns
// (CarrierBookingId, ProcessingStatus, ProcessingNote, MappedShipmentStatus, ProcessingAttempts,
// ClaimedAt, ProcessedAt) ever change.
public class CarrierTrackingEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public Guid? CarrierBookingId { get; set; }
    public required string CarrierAwb { get; set; }
    public required string CarrierEventId { get; set; }
    public required string CarrierStatusCode { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public CarrierEventSource Source { get; set; }
    public string? RawPayload { get; set; }
    public Guid? EnteredByUserId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public CarrierEventProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingNote { get; set; }
    // ShipmentStatus name (e.g. "CustomsHold") — a string so International.Domain stays
    // independent of Shipments.Domain.
    public string? MappedShipmentStatus { get; set; }
    public int ProcessingAttempts { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
