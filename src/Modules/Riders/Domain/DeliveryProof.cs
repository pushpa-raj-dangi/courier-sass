namespace Courier.Riders.Domain;

// Immutable after creation except through CorrectedAt/CorrectionReason (CLAUDE.md §18) — no
// endpoint in this phase writes those two fields (a real correction workflow is deferred, same as
// this phase deliberately not building NDR/RTO); they exist now so a later phase adding correction
// needs no migration, mirroring how Shipment.CurrentCustodianRiderId was added in Phase 6 before
// Rider existed. OtpVerified is a client-reported boolean this phase (this plan's resolved design
// decision) — no server-side OTP generate/verify flow exists yet (that needs Phase 11's real
// SMS delivery to be meaningful). Own OrganizationId for the same
// independently-queryable-audit-record reasoning as DeliveryAttempt above.
public class DeliveryProof
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid DeliveryAttemptId { get; set; }
    public Guid RiderId { get; set; }
    public bool OtpVerified { get; set; }
    public string? SignatureStorageKey { get; set; }
    public string? PhotoStorageKey { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string RecipientName { get; set; }
    public DateTimeOffset? CorrectedAt { get; set; }
    public string? CorrectionReason { get; set; }
}
