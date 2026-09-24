namespace Courier.International.Domain;

// At most one live (Pending/Booked) booking per InternationalLeg; Failed/Cancelled rows stay as
// history (I4 spec §4.2). Outbound bookings are made through the carrier; inbound bookings are
// links to the foreign carrier's existing AWB (created Booked directly).
public class CarrierBooking
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid InternationalLegId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public CarrierBookingDirection Direction { get; set; }
    public string? CarrierAwb { get; set; }
    public string? ServiceLevel { get; set; }
    public string? LabelStorageKey { get; set; }
    public CarrierBookingStatus Status { get; set; }
    public string? FailureReason { get; set; }
    // Valid JSON (jsonb) with known secret values masked — see CarrierPayloadSanitizer.
    public string? RawResponse { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset? BookedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    // Set at the handoff scan: from here the carrier is the parcel's custodian (I4 spec §3.6).
    public DateTimeOffset? CustodyStartedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
