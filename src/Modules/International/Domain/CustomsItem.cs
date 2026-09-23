namespace Courier.International.Domain;

// One row per the shipment's existing ShipmentItem, written by
// InternationalShipmentService.UpdateCustomsAsync (Task 10) — never by anything in the Shipments
// module. ShipmentItemId is validated against the shipment's real items via
// IShipmentItemLookupService (Task 8), not a DB-level FK (Shipments.ShipmentItem lives in a
// different module/DbSet ownership; the cross-module FK-to-Organization precedent in
// AppDbContext.OnModelCreating only wires Organization/Branch/Merchant-style single-owner
// references, not another module's child-entity table — see Task 5 for what IS wired).
public class CustomsItem
{
    public Guid Id { get; set; }
    public Guid InternationalLegId { get; set; }
    public Guid ShipmentItemId { get; set; }
    public required string HsCode { get; set; }
    public decimal CustomsValueAmount { get; set; }
    public required string CustomsValueCurrency { get; set; }
}
