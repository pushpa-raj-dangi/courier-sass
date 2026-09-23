using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

public partial class InternationalShipmentService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentInternationalGateway _gateway;
    private readonly ICountryLookupService _countryLookupService;
    private readonly IShipmentItemLookupService _shipmentItemLookupService;

    public InternationalShipmentService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentInternationalGateway gateway,
        ICountryLookupService countryLookupService,
        IShipmentItemLookupService shipmentItemLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _gateway = gateway;
        _countryLookupService = countryLookupService;
        _shipmentItemLookupService = shipmentItemLookupService;
    }

    public async Task<InternationalShipmentDetailResponse> CreateAsync(CreateInternationalShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new InternationalShipmentValidationException("No active organization context.");

        var foreignCountryCode = request.Direction == Shipments.Domain.ShipmentDirection.Outbound
            ? request.RecipientCountryCode
            : request.SenderCountryCode;
        if (string.IsNullOrWhiteSpace(foreignCountryCode) || !await _countryLookupService.IsServicedAsync(foreignCountryCode, cancellationToken))
        {
            throw new InternationalShipmentValidationException("Destination country is not currently serviced.");
        }

        var shellRequest = new CreateInternationalShipmentShellRequest(
            request.MerchantId, request.Direction, request.OriginBranchId, request.DestinationBranchId, request.ServiceTypeId,
            request.WeightKg, request.LengthCm, request.WidthCm, request.HeightCm,
            request.RecipientName, request.RecipientPhone, request.RecipientProvinceId, request.RecipientDistrictId, request.RecipientLocalLevelId, request.RecipientWardNumber,
            request.RecipientAddressLine, request.RecipientCity, request.RecipientRegion, request.RecipientPostalCode, request.RecipientCountryCode,
            request.SenderName, request.SenderPhone, request.SenderProvinceId, request.SenderDistrictId, request.SenderLocalLevelId, request.SenderWardNumber,
            request.SenderAddressLine, request.SenderCity, request.SenderRegion, request.SenderPostalCode, request.SenderCountryCode,
            request.ProvisionalTotalChargeAmount, request.ProvisionalTotalChargeCurrency, request.Items);

        var shipment = await _gateway.CreateShipmentShellAsync(shellRequest, actingUser, cancellationToken);

        var leg = new InternationalLeg
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = shipment.Id,
            ContentsDescription = string.Empty,
            CountryOfOrigin = string.Empty,
            Purpose = CustomsPurpose.Other,
            Incoterm = Incoterm.Ddu,
            CustomsCompletedAt = null,
            ProvisionalTotalChargeAmount = request.ProvisionalTotalChargeAmount,
            ProvisionalTotalChargeCurrency = CurrencyCode.Parse(request.ProvisionalTotalChargeCurrency).Value,
            IsProvisionalPricing = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Set<InternationalLeg>().Add(leg);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "international_leg.created", "InternationalLeg", leg.Id.ToString()), cancellationToken);

        return new InternationalShipmentDetailResponse(shipment, ToLegResponse(leg));
    }

    public async Task<InternationalLegResponse?> UpdateCustomsAsync(Guid shipmentId, UpdateCustomsRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new InternationalShipmentValidationException("No active organization context.");

        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }

        if (!Enum.TryParse<CustomsPurpose>(request.Purpose, ignoreCase: true, out var purpose))
        {
            throw new InternationalShipmentValidationException($"'{request.Purpose}' is not a valid customs purpose.");
        }
        if (!Enum.TryParse<Incoterm>(request.Incoterm, ignoreCase: true, out var incoterm))
        {
            throw new InternationalShipmentValidationException($"'{request.Incoterm}' is not a valid incoterm.");
        }
        if (request.Items.Count == 0)
        {
            throw new InternationalShipmentValidationException("At least one customs item is required.");
        }

        var shipmentItems = await _shipmentItemLookupService.GetItemsAsync(shipmentId, cancellationToken);
        var shipmentItemIds = shipmentItems.Select(i => i.Id).ToHashSet();
        foreach (var item in request.Items)
        {
            if (!shipmentItemIds.Contains(item.ShipmentItemId))
            {
                throw new InternationalShipmentValidationException($"Item {item.ShipmentItemId} does not belong to this shipment.");
            }
            CurrencyCode.Parse(item.CustomsValueCurrency);
        }

        var existingItems = await _dbContext.Set<CustomsItem>().Where(i => i.InternationalLegId == leg.Id).ToListAsync(cancellationToken);
        _dbContext.Set<CustomsItem>().RemoveRange(existingItems);
        foreach (var item in request.Items)
        {
            _dbContext.Set<CustomsItem>().Add(new CustomsItem
            {
                Id = Guid.NewGuid(),
                InternationalLegId = leg.Id,
                ShipmentItemId = item.ShipmentItemId,
                HsCode = item.HsCode,
                CustomsValueAmount = item.CustomsValueAmount,
                CustomsValueCurrency = CurrencyCode.Parse(item.CustomsValueCurrency).Value
            });
        }

        leg.ContentsDescription = request.ContentsDescription;
        leg.CountryOfOrigin = request.CountryOfOrigin.ToUpperInvariant();
        leg.Purpose = purpose;
        leg.Incoterm = incoterm;
        leg.ExportLicenceReference = request.ExportLicenceReference;
        leg.CustomsCompletedAt = DateTimeOffset.UtcNow;
        leg.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "international_leg.customs_updated", "InternationalLeg", leg.Id.ToString()), cancellationToken);

        return ToLegResponse(leg);
    }

    public async Task<ShipmentResponse?> PrepareCustomsAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new InternationalShipmentValidationException("No active organization context.");

        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        if (leg.CustomsCompletedAt is null)
        {
            throw new InternationalShipmentValidationException("Customs declaration must be completed before preparing customs.");
        }

        var result = await _gateway.TransitionToCustomsPreparedAsync(shipmentId, actingUser, cancellationToken);
        if (result is not null)
        {
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.customs_prepared", "Shipment", shipmentId.ToString()), cancellationToken);
        }
        return result;
    }

    private static InternationalLegResponse ToLegResponse(InternationalLeg leg) => new(
        leg.ShipmentId, leg.ContentsDescription, leg.CountryOfOrigin, leg.Purpose.ToString(), leg.Incoterm.ToString(),
        leg.ExportLicenceReference, leg.CustomsCompletedAt is not null, leg.ProvisionalTotalChargeAmount, leg.ProvisionalTotalChargeCurrency, leg.IsProvisionalPricing);
}
