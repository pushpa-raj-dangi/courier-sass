namespace Courier.Shipments.Domain;

// All charge fields are snapshotted once at booking from PricingService.QuoteAsync and never
// recalculated (CLAUDE.md §16) — a later RateCard version change must not alter this shipment's
// price. CurrentCustodianRiderId is always null until Phase 8 introduces Rider; kept as a real
// column now so that phase needs no migration to add it (see this plan's Architecture section).
public class Shipment
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Awb { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OriginBranchId { get; set; }
    public Guid DestinationBranchId { get; set; }
    public Guid? CurrentBranchId { get; set; }
    public Guid? CurrentCustodianUserId { get; set; }
    public Guid? CurrentCustodianRiderId { get; set; }
    public ShipmentStatus CurrentStatus { get; set; }
    public Guid ServiceTypeId { get; set; }

    // I3 — set once at creation, never changed afterward. Default Domestic/Domestic so every
    // pre-I3 row (via migration default) and every domestic creation path is unaffected.
    public ShipmentScope Scope { get; set; } = ShipmentScope.Domestic;
    public ShipmentDirection Direction { get; set; } = ShipmentDirection.Domestic;

    public decimal WeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    public decimal DeclaredValue { get; set; }
    public decimal CodAmount { get; set; }
    public bool WantsInsurance { get; set; }

    public decimal DeliveryCharge { get; set; }
    public decimal InsuranceCharge { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal TotalCharge { get; set; }

    public required string RecipientName { get; set; }
    public required string RecipientPhone { get; set; }
    // I3 — nullable: a domestic shipment or the domestic-side party of an international shipment
    // populates all four; the foreign-side party of an international shipment leaves all four
    // null and populates RecipientCity/RecipientCountryCode/etc. instead. See
    // ShipmentAddressValidator for the enforcement.
    public Guid? RecipientProvinceId { get; set; }
    public Guid? RecipientDistrictId { get; set; }
    public Guid? RecipientLocalLevelId { get; set; }
    public int? RecipientWardNumber { get; set; }
    // Reused as InternationalAddress.Line1 when this party is the foreign side — avoids a
    // duplicate Line1 column (I3 design spec §3).
    public required string RecipientAddressLine { get; set; }
    public string? RecipientCity { get; set; }
    public string? RecipientRegion { get; set; }
    public string? RecipientPostalCode { get; set; }
    public string? RecipientCountryCode { get; set; }

    public required string SenderName { get; set; }
    public required string SenderPhone { get; set; }
    public Guid? SenderProvinceId { get; set; }
    public Guid? SenderDistrictId { get; set; }
    public Guid? SenderLocalLevelId { get; set; }
    public int? SenderWardNumber { get; set; }
    public required string SenderAddressLine { get; set; }
    public string? SenderCity { get; set; }
    public string? SenderRegion { get; set; }
    public string? SenderPostalCode { get; set; }
    public string? SenderCountryCode { get; set; }
}
