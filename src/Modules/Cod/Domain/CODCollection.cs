namespace Courier.Cod.Domain;

// Created eagerly by Cod.Application.CodService.CreateExpectedAsync, called from
// Shipments.Application.ShipmentService.CreateShipmentAsync immediately after a shipment with
// CodAmount > 0 is persisted (Task 4/5) — this plan's resolved design decision (bounded brainstorm):
// eager creation at booking, not lazy creation at delivery, so "Expected COD" reporting is a direct
// query against this table instead of a Shipment-table fallback, and a DB unique index enforces "at
// most one CODCollection per shipment" from day one (see CodCollectionConfiguration, Task 2).
//
// MerchantId, BranchId, and ShipmentTotalCharge are snapshotted here at creation time rather than
// looked up from Shipments later — Cod has no project reference to Shipments at all (see this
// plan's Global Constraints). BranchId is the shipment's DestinationBranchId at booking time (the
// branch that will attempt delivery and collect the cash) — the same branch Ndr/Riders already
// scope NDR/delivery-attempt operations to.
public class CODCollection
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid BranchId { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal ShipmentTotalCharge { get; set; }
    public decimal? CollectedAmount { get; set; }
    public CodCollectionStatus Status { get; set; }
    public Guid? CollectedByRiderId { get; set; }
    public DateTimeOffset? CollectedAt { get; set; }
    public Guid? DeliveryProofId { get; set; }
    public Guid? RiderCashSettlementId { get; set; }
    public Guid? MerchantSettlementItemId { get; set; }
}
