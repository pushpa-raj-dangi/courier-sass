using Courier.Pricing.Domain;

namespace Courier.Pricing.Application;

public record CreateRateCardRequest(string Name, DateTimeOffset EffectiveFrom);

public record RateCardResponse(Guid Id, string Name, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, int Version, bool IsActive);

public record CreateZoneRateRequest(Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId, Guid WeightSlabId, decimal BaseRate);

public record ZoneRateResponse(Guid Id, Guid RateCardId, Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId, Guid WeightSlabId, decimal BaseRate);

public record CreateAdditionalChargeRequest(AdditionalChargeType Type, ChargeCalculationType CalculationType, decimal Value);

public record AdditionalChargeResponse(Guid Id, Guid RateCardId, AdditionalChargeType Type, ChargeCalculationType CalculationType, decimal Value);

public record UpsertMerchantRateCardRequest(Guid RateCardId, decimal? OverrideDiscountPercent);

public record MerchantRateCardResponse(Guid Id, Guid MerchantId, Guid RateCardId, decimal? OverrideDiscountPercent);

public record ServiceTypeResponse(Guid Id, ServiceTypeCode Code, string Name);

public record WeightSlabResponse(Guid Id, decimal MinWeightKg, decimal? MaxWeightKg);

public class PricingValidationException : Exception
{
    public PricingValidationException(string message) : base(message) { }
}
