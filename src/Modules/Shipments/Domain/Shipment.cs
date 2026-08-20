namespace Courier.Shipments.Domain;

// All charge fields are snapshotted once at booking from PricingService.QuoteAsync and never
// recalculated (CLAUDE.md §16) — a later RateCard version change must not alter this shipment's
// price. CurrentCustodianRiderId is always null until Phase 8 introduces Rider; kept as a real
// column now so that phase needs no migration to add it (see this plan's Architecture section).
public class Shipment
{
    public Guid Id { get; set; }
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
    public required Guid RecipientProvinceId { get; set; }
    public required Guid RecipientDistrictId { get; set; }
    public required Guid RecipientLocalLevelId { get; set; }
    public required int RecipientWardNumber { get; set; }
    public required string RecipientAddressLine { get; set; }

    public required string SenderName { get; set; }
    public required string SenderPhone { get; set; }
    public required Guid SenderProvinceId { get; set; }
    public required Guid SenderDistrictId { get; set; }
    public required Guid SenderLocalLevelId { get; set; }
    public required int SenderWardNumber { get; set; }
    public required string SenderAddressLine { get; set; }
}
