namespace Courier.International.Domain;

// One-to-one with Shipment by ShipmentId (I3 design spec §3). Tenant-owned — unlike Country/FxRate
// (platform-level reference data), this is shipment-owned data and carries OrganizationId + the
// standard tenant query filter (Task 5). ContentsDescription/CountryOfOrigin start empty and
// CustomsCompletedAt stays null until InternationalShipmentService.UpdateCustomsAsync (Task 10)
// completes the declaration — that's what gates the CustomsPrepared transition.
public class InternationalLeg
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public string ContentsDescription { get; set; } = string.Empty;
    public string CountryOfOrigin { get; set; } = string.Empty;
    public CustomsPurpose Purpose { get; set; }
    public Incoterm Incoterm { get; set; }
    public string? ExportLicenceReference { get; set; }
    public DateTimeOffset? CustomsCompletedAt { get; set; }
    // No real pricing engine exists yet (I5) — this is a staff-entered placeholder, always
    // IsProvisionalPricing = true in this plan. See design spec §2 "Pricing".
    public decimal ProvisionalTotalChargeAmount { get; set; }
    public required string ProvisionalTotalChargeCurrency { get; set; }
    public bool IsProvisionalPricing { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
