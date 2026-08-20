using Courier.Pricing.Domain;

namespace Courier.Pricing.Application;

public record PriceQuoteRequest(
    Guid OriginBranchId,
    Guid DestinationBranchId,
    decimal WeightKg,
    Guid ServiceTypeId,
    decimal CodAmount,
    decimal DeclaredValue,
    bool WantsInsurance,
    Guid? MerchantId);

public record AppliedCharge(AdditionalChargeType Type, decimal Amount);

public record PriceQuoteResponse(
    decimal BaseRate,
    IReadOnlyList<AppliedCharge> AppliedCharges,
    decimal DiscountPercent,
    decimal Subtotal,
    decimal Total,
    Guid RateCardId,
    int RateCardVersion);
