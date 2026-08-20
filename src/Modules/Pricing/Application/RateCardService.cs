using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public class RateCardService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IGeographyLookupService _geographyLookupService;
    private readonly IMerchantLookupService _merchantLookupService;

    public RateCardService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IGeographyLookupService geographyLookupService,
        IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _geographyLookupService = geographyLookupService;
        _merchantLookupService = merchantLookupService;
    }

    public async Task<RateCardResponse> CreateRateCardAsync(CreateRateCardRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        var previous = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.IsActive, cancellationToken);

        var rateCard = new RateCard
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = null,
            Version = previous is null ? 1 : previous.Version + 1,
            IsActive = true
        };
        _dbContext.Set<RateCard>().Add(rateCard);

        if (previous is not null)
        {
            previous.IsActive = false;
            previous.EffectiveTo = request.EffectiveFrom;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "rate_card.created", "RateCard", rateCard.Id.ToString()), cancellationToken);

        return ToResponse(rateCard);
    }

    public async Task<RateCardResponse?> GetRateCardAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken);
        return rateCard is null ? null : ToResponse(rateCard);
    }

    public async Task<List<RateCardResponse>> ListRateCardsAsync(CancellationToken cancellationToken = default)
    {
        var rateCards = await _dbContext.Set<RateCard>().OrderByDescending(rc => rc.Version).ToListAsync(cancellationToken);
        return rateCards.Select(ToResponse).ToList();
    }

    public async Task<ZoneRateResponse> AddZoneRateAsync(Guid rateCardId, CreateZoneRateRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        var rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken)
            ?? throw new PricingValidationException("Rate card was not found in this organization.");
        if (!rateCard.IsActive)
        {
            throw new PricingValidationException("Cannot add a zone rate to a superseded rate card.");
        }

        if (!await _geographyLookupService.DeliveryZoneExistsAsync(request.OriginZoneId, cancellationToken))
        {
            throw new PricingValidationException("Origin delivery zone was not found in this organization.");
        }
        if (!await _geographyLookupService.DeliveryZoneExistsAsync(request.DestinationZoneId, cancellationToken))
        {
            throw new PricingValidationException("Destination delivery zone was not found in this organization.");
        }
        if (!await _dbContext.Set<ServiceType>().AnyAsync(s => s.Id == request.ServiceTypeId, cancellationToken))
        {
            throw new PricingValidationException("Unknown service type.");
        }
        if (!await _dbContext.Set<WeightSlab>().AnyAsync(w => w.Id == request.WeightSlabId, cancellationToken))
        {
            throw new PricingValidationException("Unknown weight slab.");
        }

        var zoneRate = new ZoneRate
        {
            Id = Guid.NewGuid(),
            RateCardId = rateCardId,
            OriginZoneId = request.OriginZoneId,
            DestinationZoneId = request.DestinationZoneId,
            ServiceTypeId = request.ServiceTypeId,
            WeightSlabId = request.WeightSlabId,
            BaseRate = request.BaseRate
        };
        _dbContext.Set<ZoneRate>().Add(zoneRate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "zone_rate.created", "ZoneRate", zoneRate.Id.ToString()), cancellationToken);

        return ToResponse(zoneRate);
    }

    public async Task<List<ZoneRateResponse>> ListZoneRatesAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var zoneRates = await _dbContext.Set<ZoneRate>().Where(z => z.RateCardId == rateCardId).ToListAsync(cancellationToken);
        return zoneRates.Select(ToResponse).ToList();
    }

    public async Task<AdditionalChargeResponse> AddAdditionalChargeAsync(Guid rateCardId, CreateAdditionalChargeRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        var rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken)
            ?? throw new PricingValidationException("Rate card was not found in this organization.");
        if (!rateCard.IsActive)
        {
            throw new PricingValidationException("Cannot add an additional charge to a superseded rate card.");
        }

        var charge = new AdditionalCharge
        {
            Id = Guid.NewGuid(),
            RateCardId = rateCardId,
            Type = request.Type,
            CalculationType = request.CalculationType,
            Value = request.Value
        };
        _dbContext.Set<AdditionalCharge>().Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "additional_charge.created", "AdditionalCharge", charge.Id.ToString()), cancellationToken);

        return ToResponse(charge);
    }

    public async Task<List<AdditionalChargeResponse>> ListAdditionalChargesAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var charges = await _dbContext.Set<AdditionalCharge>().Where(c => c.RateCardId == rateCardId).ToListAsync(cancellationToken);
        return charges.Select(ToResponse).ToList();
    }

    public async Task<MerchantRateCardResponse> UpsertMerchantRateCardAsync(Guid merchantId, UpsertMerchantRateCardRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        if (!await _merchantLookupService.ExistsAsync(merchantId, cancellationToken))
        {
            throw new PricingValidationException("Merchant was not found in this organization.");
        }
        if (!await _dbContext.Set<RateCard>().AnyAsync(rc => rc.Id == request.RateCardId, cancellationToken))
        {
            throw new PricingValidationException("Rate card was not found in this organization.");
        }

        var existing = await _dbContext.Set<MerchantRateCard>().FirstOrDefaultAsync(m => m.MerchantId == merchantId, cancellationToken);
        if (existing is null)
        {
            existing = new MerchantRateCard
            {
                Id = Guid.NewGuid(),
                MerchantId = merchantId,
                OrganizationId = organizationId,
                RateCardId = request.RateCardId,
                OverrideDiscountPercent = request.OverrideDiscountPercent
            };
            _dbContext.Set<MerchantRateCard>().Add(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant_rate_card.created", "MerchantRateCard", existing.Id.ToString()), cancellationToken);
        }
        else
        {
            existing.RateCardId = request.RateCardId;
            existing.OverrideDiscountPercent = request.OverrideDiscountPercent;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant_rate_card.updated", "MerchantRateCard", existing.Id.ToString()), cancellationToken);
        }

        return ToResponse(existing);
    }

    public async Task<MerchantRateCardResponse?> GetMerchantRateCardAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var merchantRateCard = await _dbContext.Set<MerchantRateCard>().FirstOrDefaultAsync(m => m.MerchantId == merchantId, cancellationToken);
        return merchantRateCard is null ? null : ToResponse(merchantRateCard);
    }

    private static RateCardResponse ToResponse(RateCard rateCard) =>
        new(rateCard.Id, rateCard.Name, rateCard.EffectiveFrom, rateCard.EffectiveTo, rateCard.Version, rateCard.IsActive);

    private static ZoneRateResponse ToResponse(ZoneRate zoneRate) =>
        new(zoneRate.Id, zoneRate.RateCardId, zoneRate.OriginZoneId, zoneRate.DestinationZoneId, zoneRate.ServiceTypeId, zoneRate.WeightSlabId, zoneRate.BaseRate);

    private static AdditionalChargeResponse ToResponse(AdditionalCharge charge) =>
        new(charge.Id, charge.RateCardId, charge.Type, charge.CalculationType, charge.Value);

    private static MerchantRateCardResponse ToResponse(MerchantRateCard merchantRateCard) =>
        new(merchantRateCard.Id, merchantRateCard.MerchantId, merchantRateCard.RateCardId, merchantRateCard.OverrideDiscountPercent);
}
