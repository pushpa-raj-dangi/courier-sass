namespace Courier.Pricing.Domain;

// At most one row per MerchantId (enforced by a unique index — see MerchantRateCardConfiguration).
// A merchant with no row here uses their org's current default RateCard with no discount.
public class MerchantRateCard
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RateCardId { get; set; }
    public decimal? OverrideDiscountPercent { get; set; }
}
