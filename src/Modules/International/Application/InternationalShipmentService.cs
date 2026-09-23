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

    public InternationalShipmentService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentInternationalGateway gateway,
        ICountryLookupService countryLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _gateway = gateway;
        _countryLookupService = countryLookupService;
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

    private static InternationalLegResponse ToLegResponse(InternationalLeg leg) => new(
        leg.ShipmentId, leg.ContentsDescription, leg.CountryOfOrigin, leg.Purpose.ToString(), leg.Incoterm.ToString(),
        leg.ExportLicenceReference, leg.CustomsCompletedAt is not null, leg.ProvisionalTotalChargeAmount, leg.ProvisionalTotalChargeCurrency, leg.IsProvisionalPricing);
}
