namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateBranchSettlementAsync (Task 7). Deliberately has no
// RiderCashSettlementIds[] array column, unlike DOMAIN-MODEL.md's original sketch — the covered
// rider settlements are the RiderCashSettlement rows whose BranchCashSettlementId points back here
// (a child-side FK), matching this codebase's established one-to-many convention
// (ManifestShipment.ManifestId, DeliveryTask.DeliveryRunId) instead of a Postgres array column.
// This plan's resolved design decision (bounded brainstorm).
public class BranchCashSettlement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid BranchId { get; set; }
    public decimal ConfirmedAmount { get; set; }
    public Guid ReconciledByUserId { get; set; }
    public DateTimeOffset ReconciledAt { get; set; }
}
