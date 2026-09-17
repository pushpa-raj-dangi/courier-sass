namespace Courier.Riders.Domain;

// No own OrganizationId — tenant isolation flows transitively through
// RiderId -> Rider.OrganizationId, the same pattern DeliveryTask already established (via
// DeliveryRunId -> DeliveryRun -> RiderId). DeliveryRunId is nullable: a rider can have the PWA
// open with no active run yet (before their first task of the day is assigned).
// RecordedAt is client-reported (the device's own GPS timestamp) — trusted only for ordering
// within a run's breadcrumb, never for anything security- or billing-relevant, matching this
// codebase's posture on other client-reported fields (e.g. DeliveryProof.OtpVerified).
// ReceivedAt is set server-side (DateTimeOffset.UtcNow at insert, in RiderService.Location.cs) and
// is never taken from the request body — it anchors "last seen" age display and retention pruning
// to a clock the rider's device cannot skew.
public class RiderLocationPing
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid? DeliveryRunId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? AccuracyMeters { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
