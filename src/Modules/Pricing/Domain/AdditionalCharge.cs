namespace Courier.Pricing.Domain;

// Same no-own-OrganizationId reasoning as ZoneRate. Type drives whether the pricing engine
// applies this charge to a given quote (Fuel/Other always; Insurance only if the request opts
// in; COD only if CodAmount > 0) — see Pricing.Application.PricingService.
public class AdditionalCharge
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public AdditionalChargeType Type { get; set; }
    public ChargeCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }
}
