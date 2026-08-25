namespace Courier.Cod.Domain;

// One row per shipment folded into a MerchantSettlement. CODAmount/CourierFee are copied from the
// covering CODCollection's ExpectedAmount/ShipmentTotalCharge at settlement-creation time (both
// already locked at booking, see CODCollection's doc comment) — CourierFee is never recomputed
// from a current rate card. OtherDeductions defaults to 0 and is the only field
// Cod.Application.CodService.ApproveAdjustmentAsync (Task 10) is ever allowed to rewrite, which
// also recomputes NetPayable at the same time. No own OrganizationId — tenant isolation flows
// transitively through MerchantSettlementId -> MerchantSettlement.OrganizationId, same pattern as
// ShipmentItem -> Shipment / ZoneRate -> RateCard.
public class MerchantSettlementItem
{
    public Guid Id { get; set; }
    public Guid MerchantSettlementId { get; set; }
    public Guid ShipmentId { get; set; }
    public decimal CODAmount { get; set; }
    public decimal CourierFee { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPayable { get; set; }
}
