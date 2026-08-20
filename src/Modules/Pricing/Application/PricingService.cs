using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public class PricingService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IMerchantLookupService _merchantLookupService;

    public PricingService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IBranchLookupService branchLookupService,
        IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _branchLookupService = branchLookupService;
        _merchantLookupService = merchantLookupService;
    }

    // Deterministic charge-evaluation order: Fuel/Other apply unconditionally, before the
    // input-driven Insurance/COD charges, so applied-charge order (and therefore any
    // Fuel/Other percentage compounding against the running subtotal) never depends on how the
    // RateCard's rows happen to be stored.
    private static readonly AdditionalChargeType[] ChargeEvaluationOrder =
    {
        AdditionalChargeType.Fuel,
        AdditionalChargeType.Other,
        AdditionalChargeType.Insurance,
        AdditionalChargeType.COD
    };

    public async Task<PriceQuoteResponse> QuoteAsync(PriceQuoteRequest request, CancellationToken cancellationToken = default)
    {
        _ = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");

        var originZone = await _branchLookupService.GetZoneInfoAsync(request.OriginBranchId, cancellationToken)
            ?? throw new PricingValidationException("Origin branch was not found in this organization.");
        var destinationZone = await _branchLookupService.GetZoneInfoAsync(request.DestinationBranchId, cancellationToken)
            ?? throw new PricingValidationException("Destination branch was not found in this organization.");

        if (originZone.DeliveryZoneId is null || destinationZone.DeliveryZoneId is null)
        {
            throw new PricingValidationException("Origin or destination branch has no delivery zone assigned.");
        }

        var weightSlab = await _dbContext.Set<WeightSlab>()
            .Where(w => w.MinWeightKg <= request.WeightKg && (w.MaxWeightKg == null || request.WeightKg < w.MaxWeightKg))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PricingValidationException($"No weight slab covers {request.WeightKg}kg.");

        RateCard? rateCard;
        var discountPercent = 0m;

        if (request.MerchantId is Guid merchantId)
        {
            if (!await _merchantLookupService.ExistsAsync(merchantId, cancellationToken))
            {
                throw new PricingValidationException("Merchant was not found in this organization.");
            }

            var merchantRateCard = await _dbContext.Set<MerchantRateCard>().FirstOrDefaultAsync(m => m.MerchantId == merchantId, cancellationToken);
            if (merchantRateCard is not null)
            {
                rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == merchantRateCard.RateCardId, cancellationToken);
                discountPercent = merchantRateCard.OverrideDiscountPercent ?? 0m;
            }
            else
            {
                rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.IsActive, cancellationToken);
            }
        }
        else
        {
            rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.IsActive, cancellationToken);
        }

        if (rateCard is null)
        {
            throw new PricingValidationException("No active rate card found for this organization.");
        }

        var zoneRate = await _dbContext.Set<ZoneRate>().FirstOrDefaultAsync(z =>
                z.RateCardId == rateCard.Id &&
                z.OriginZoneId == originZone.DeliveryZoneId &&
                z.DestinationZoneId == destinationZone.DeliveryZoneId &&
                z.ServiceTypeId == request.ServiceTypeId &&
                z.WeightSlabId == weightSlab.Id, cancellationToken)
            ?? throw new PricingValidationException("No rate is defined for this origin/destination/service/weight combination.");

        var charges = await _dbContext.Set<AdditionalCharge>().Where(c => c.RateCardId == rateCard.Id).ToListAsync(cancellationToken);

        var runningSubtotal = zoneRate.BaseRate;
        var applied = new List<AppliedCharge>();

        foreach (var chargeType in ChargeEvaluationOrder)
        {
            foreach (var charge in charges.Where(c => c.Type == chargeType))
            {
                var appliesNow = chargeType switch
                {
                    AdditionalChargeType.Insurance => request.WantsInsurance,
                    AdditionalChargeType.COD => request.CodAmount > 0,
                    _ => true
                };
                if (!appliesNow)
                {
                    continue;
                }

                var baseAmount = chargeType switch
                {
                    AdditionalChargeType.Insurance => request.DeclaredValue,
                    AdditionalChargeType.COD => request.CodAmount,
                    _ => runningSubtotal
                };

                var amount = charge.CalculationType == ChargeCalculationType.Flat
                    ? charge.Value
                    : Math.Round(charge.Value / 100m * baseAmount, 2, MidpointRounding.AwayFromZero);

                applied.Add(new AppliedCharge(charge.Type, amount));
                runningSubtotal += amount;
            }
        }

        var total = Math.Round(runningSubtotal * (1 - discountPercent / 100m), 2, MidpointRounding.AwayFromZero);

        return new PriceQuoteResponse(zoneRate.BaseRate, applied, discountPercent, runningSubtotal, total, rateCard.Id, rateCard.Version);
    }
}
