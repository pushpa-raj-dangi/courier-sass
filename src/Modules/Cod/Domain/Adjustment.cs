namespace Courier.Cod.Domain;

// Created unapplied by Cod.Application.CodService.RequestAdjustmentAsync, applied only by a
// separate CodService.ApproveAdjustmentAsync call (Task 10) — this plan's resolved design decision
// (bounded brainstorm): Adjustment always requires a separate approval step, since no
// configurable-threshold infrastructure exists yet to decide when one is "appropriate"
// (CLAUDE.md §23). v1 supports EntityType = "MerchantSettlementItem" only, targeting
// OtherDeductions — the only adjustment case with a concrete business need right now; Applied
// stays false until ApproveAdjustmentAsync actually rewrites the target row.
public class Adjustment
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public decimal BeforeValue { get; set; }
    public decimal AfterValue { get; set; }
    public required string Reason { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public bool Applied { get; set; }
}
