namespace Courier.Riders.Domain;

// One row per real-world attempt, success or failure (DOMAIN-MODEL.md: "Shipment 1─* DeliveryAttempt
// 1─(0..1)─ DeliveryProof" — a DeliveryProof exists iff Succeeded). Succeeded is an additive field
// beyond DOMAIN-MODEL.md's original field sketch (which only lists Reason) — needed to represent
// both outcomes in one table without a nullable-Reason-means-success convention that would be easy
// to get backwards; Reason is null iff Succeeded is true. NextAction is a plain string, not a
// controlled enum (this plan's resolved design decision) — Phase 9's NDR workflow is what actually
// automates "what happens next", this phase just records what the rider wrote down.
// Own OrganizationId (unlike DeliveryRun/DeliveryTask): this is a durable, independently-queryable
// audit/financial-adjacent record CLAUDE.md §19/§28 treats seriously enough to list on its own,
// and a future rider-performance report (Phase 13) plausibly queries it without going through a
// specific Shipment first.
public class DeliveryAttempt
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid RiderId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public bool Succeeded { get; set; }
    public DeliveryAttemptReason? Reason { get; set; }
    public string? Notes { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool CustomerContacted { get; set; }
    public string? NextAction { get; set; }
}
