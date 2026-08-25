namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateRiderSettlementAsync (Task 6) — a rider's end-of-run
// cash handover, covering every one of that rider's CodCollected CODCollection rows at the time of
// the call.
public class RiderCashSettlement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RiderId { get; set; }
    public Guid BranchId { get; set; }
    public decimal DeclaredAmount { get; set; }
    public int ShipmentCount { get; set; }
    public DateTimeOffset SettledAt { get; set; }
    public Guid ConfirmedByUserId { get; set; }
    public Guid? BranchCashSettlementId { get; set; }
}
