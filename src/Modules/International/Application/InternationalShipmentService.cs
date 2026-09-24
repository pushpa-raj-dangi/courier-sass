using System.Security.Claims;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

public partial class InternationalShipmentService
{
    // Column limits from InternationalLegConfiguration / CustomsItemConfiguration and the foreign
    // address columns in ShipmentConfiguration — checked here so oversize input is a 400, not a
    // DbUpdateException 500.
    private const int MaxContentsDescriptionLength = 1000;
    private const int MaxExportLicenceReferenceLength = 200;
    private const int MaxHsCodeLength = 20;
    private const int MaxForeignCityLength = 200;
    private const int MaxForeignRegionLength = 200;
    private const int MaxForeignPostalCodeLength = 20;

    // The customs declaration is editable only while the shipment has not yet reached
    // CustomsPrepared (I5 ruling) — i.e. in the statuses from which CustomsPrepared is still ahead.
    private static readonly HashSet<ShipmentStatus> CustomsEditableStatuses = new()
    {
        ShipmentStatus.Created,
        ShipmentStatus.PickupAssigned,
        ShipmentStatus.PickedUp,
        ShipmentStatus.AtOriginBranch,
        ShipmentStatus.Sorted,
        ShipmentStatus.InTransit,
        ShipmentStatus.AtDestinationBranch
    };

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentInternationalGateway _gateway;
    private readonly ICountryLookupService _countryLookupService;
    private readonly IShipmentItemLookupService _shipmentItemLookupService;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public InternationalShipmentService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentInternationalGateway gateway,
        ICountryLookupService countryLookupService,
        IShipmentItemLookupService shipmentItemLookupService,
        IShipmentTransitionService shipmentTransitionService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _gateway = gateway;
        _countryLookupService = countryLookupService;
        _shipmentItemLookupService = shipmentItemLookupService;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<InternationalShipmentDetailResponse> CreateAsync(CreateInternationalShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new InternationalShipmentValidationException("No active organization context.");

        // Every International-side validation runs before the gateway is called, so nothing is
        // written for a request this service would reject.
        var chargeCurrency = ValidateCreateRequest(request);

        var foreignCountryCode = request.Direction == ShipmentDirection.Outbound
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
            request.ProvisionalTotalChargeAmount, chargeCurrency, request.Items);

        // One transaction for the gateway's Shipment shell (its own SaveChangesAsync calls, audit
        // and shipment.created outbox row) plus the InternationalLeg and its audit — design spec §4.
        // DbContext is scoped and is the same AppDbContext instance ShipmentService holds
        // (Program.cs registers DbContext as an alias of AppDbContext), so the gateway's writes
        // enlist in this transaction. The gateway's AWB collision-retry loop keeps working inside
        // it: with a transaction already active, EF Core wraps each SaveChangesAsync in a savepoint
        // and rolls back to it on failure, so a unique-violation doesn't abort the outer
        // transaction. Same IsRelational() guard as CodService.Settlements.cs / AuthService.cs —
        // the InMemory provider used by unit tests has no transactions.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
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
                ProvisionalTotalChargeCurrency = chargeCurrency,
                IsProvisionalPricing = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<InternationalLeg>().Add(leg);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "international_leg.created", "InternationalLeg", leg.Id.ToString()), cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new InternationalShipmentDetailResponse(shipment, InternationalShipmentAccess.ToLegResponse(leg, Array.Empty<CustomsItem>()));
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
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
        var shipment = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (shipment is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, "international.manage_customs",
                shipment.OriginBranchId, shipment.DestinationBranchId, shipment.CurrentBranchId, cancellationToken))
        {
            return null;
        }
        if (!CustomsEditableStatuses.Contains(shipment.CurrentStatus))
        {
            throw new InternationalShipmentValidationException(
                $"The customs declaration can no longer be edited: shipment is {shipment.CurrentStatus} (editable only before CustomsPrepared).");
        }

        var validated = await ValidateCustomsRequestAsync(request, shipmentId, cancellationToken);

        var existingItems = await _dbContext.Set<CustomsItem>().Where(i => i.InternationalLegId == leg.Id).ToListAsync(cancellationToken);
        var before = leg.CustomsCompletedAt is null ? null : SerializeDeclaration(leg, existingItems);

        _dbContext.Set<CustomsItem>().RemoveRange(existingItems);
        var newItems = validated.Items.Select(item => new CustomsItem
        {
            Id = Guid.NewGuid(),
            InternationalLegId = leg.Id,
            ShipmentItemId = item.ShipmentItemId,
            HsCode = item.HsCode,
            CustomsValueAmount = item.CustomsValueAmount,
            CustomsValueCurrency = item.CustomsValueCurrency
        }).ToList();
        _dbContext.Set<CustomsItem>().AddRange(newItems);

        leg.ContentsDescription = request.ContentsDescription.Trim();
        leg.CountryOfOrigin = validated.CountryOfOrigin;
        leg.Purpose = validated.Purpose;
        leg.Incoterm = validated.Incoterm;
        leg.ExportLicenceReference = string.IsNullOrWhiteSpace(request.ExportLicenceReference) ? null : request.ExportLicenceReference.Trim();
        leg.CustomsCompletedAt = DateTimeOffset.UtcNow;
        leg.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "international_leg.customs_updated", "InternationalLeg", leg.Id.ToString(),
            Before: before, After: SerializeDeclaration(leg, newItems)), cancellationToken);

        return InternationalShipmentAccess.ToLegResponse(leg, newItems.OrderBy(i => i.ShipmentItemId));
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
        // Visibility (404) first, using the same origin/destination/current rule as GET/PATCH; the
        // stricter "must be at the branch currently holding the parcel" check happens in the
        // gateway (400 SHIPMENT_VALIDATION_FAILED, same as a scan at the wrong branch).
        var shipment = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (shipment is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, "international.manage_customs",
                shipment.OriginBranchId, shipment.DestinationBranchId, shipment.CurrentBranchId, cancellationToken))
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

    // Returns the normalized (upper-case) provisional-charge currency.
    private static string ValidateCreateRequest(CreateInternationalShipmentRequest request)
    {
        if (request.Direction is not (ShipmentDirection.Outbound or ShipmentDirection.Inbound))
        {
            throw new InternationalShipmentValidationException("Direction must be Outbound or Inbound.");
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new InternationalShipmentValidationException("At least one shipment item is required.");
        }
        if (request.Items.Any(i => i is null))
        {
            throw new InternationalShipmentValidationException("Shipment items must not contain null entries.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            throw new InternationalShipmentValidationException("Item quantity must be greater than zero.");
        }
        if (request.Items.Any(i => i.UnitValue < 0))
        {
            throw new InternationalShipmentValidationException("Item unit value must not be negative.");
        }
        if (request.WeightKg <= 0)
        {
            throw new InternationalShipmentValidationException("WeightKg must be greater than zero.");
        }
        if (request.ProvisionalTotalChargeAmount < 0)
        {
            throw new InternationalShipmentValidationException("ProvisionalTotalChargeAmount must not be negative.");
        }
        if (!CurrencyCode.TryParse(request.ProvisionalTotalChargeCurrency, out var currency))
        {
            throw new InternationalShipmentValidationException($"'{request.ProvisionalTotalChargeCurrency}' is not a valid three-letter currency code.");
        }

        var (party, city, region, postalCode) = request.Direction == ShipmentDirection.Outbound
            ? ("Recipient", request.RecipientCity, request.RecipientRegion, request.RecipientPostalCode)
            : ("Sender", request.SenderCity, request.SenderRegion, request.SenderPostalCode);
        if (city is { Length: > MaxForeignCityLength })
        {
            throw new InternationalShipmentValidationException($"{party} city must be at most {MaxForeignCityLength} characters.");
        }
        if (region is { Length: > MaxForeignRegionLength })
        {
            throw new InternationalShipmentValidationException($"{party} region must be at most {MaxForeignRegionLength} characters.");
        }
        if (postalCode is { Length: > MaxForeignPostalCodeLength })
        {
            throw new InternationalShipmentValidationException($"{party} postal code must be at most {MaxForeignPostalCodeLength} characters.");
        }

        return currency.Value;
    }

    private sealed record ValidatedCustomsItem(Guid ShipmentItemId, string HsCode, decimal CustomsValueAmount, string CustomsValueCurrency);
    private sealed record ValidatedCustoms(string CountryOfOrigin, CustomsPurpose Purpose, Incoterm Incoterm, List<ValidatedCustomsItem> Items);

    private async Task<ValidatedCustoms> ValidateCustomsRequestAsync(UpdateCustomsRequest request, Guid shipmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContentsDescription))
        {
            throw new InternationalShipmentValidationException("ContentsDescription is required.");
        }
        if (request.ContentsDescription.Trim().Length > MaxContentsDescriptionLength)
        {
            throw new InternationalShipmentValidationException($"ContentsDescription must be at most {MaxContentsDescriptionLength} characters.");
        }
        if (request.ExportLicenceReference is { } licence && licence.Trim().Length > MaxExportLicenceReferenceLength)
        {
            throw new InternationalShipmentValidationException($"ExportLicenceReference must be at most {MaxExportLicenceReferenceLength} characters.");
        }
        if (!Enum.TryParse<CustomsPurpose>(request.Purpose, ignoreCase: true, out var purpose) || !Enum.IsDefined(purpose))
        {
            throw new InternationalShipmentValidationException($"'{request.Purpose}' is not a valid customs purpose.");
        }
        if (!Enum.TryParse<Incoterm>(request.Incoterm, ignoreCase: true, out var incoterm) || !Enum.IsDefined(incoterm))
        {
            throw new InternationalShipmentValidationException($"'{request.Incoterm}' is not a valid incoterm.");
        }
        if (request.CountryOfOrigin is not { Length: 2 } countryOfOrigin || !countryOfOrigin.All(char.IsAsciiLetter)
            || !await _countryLookupService.ExistsAsync(countryOfOrigin, cancellationToken))
        {
            throw new InternationalShipmentValidationException($"'{request.CountryOfOrigin}' is not a known two-letter country code.");
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new InternationalShipmentValidationException("At least one customs item is required.");
        }

        var shipmentItemIds = (await _shipmentItemLookupService.GetItemsAsync(shipmentId, cancellationToken)).Select(i => i.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        var items = new List<ValidatedCustomsItem>();
        foreach (var item in request.Items)
        {
            if (item is null)
            {
                throw new InternationalShipmentValidationException("Customs items must not be null.");
            }
            if (!shipmentItemIds.Contains(item.ShipmentItemId))
            {
                throw new InternationalShipmentValidationException($"Item {item.ShipmentItemId} does not belong to this shipment.");
            }
            if (!seen.Add(item.ShipmentItemId))
            {
                throw new InternationalShipmentValidationException($"Item {item.ShipmentItemId} appears more than once; send exactly one customs item per shipment item.");
            }
            if (string.IsNullOrWhiteSpace(item.HsCode))
            {
                throw new InternationalShipmentValidationException($"HsCode is required for item {item.ShipmentItemId}.");
            }
            var hsCode = item.HsCode.Trim();
            if (hsCode.Length > MaxHsCodeLength)
            {
                throw new InternationalShipmentValidationException($"HsCode must be at most {MaxHsCodeLength} characters.");
            }
            if (item.CustomsValueAmount < 0)
            {
                throw new InternationalShipmentValidationException($"CustomsValueAmount must not be negative for item {item.ShipmentItemId}.");
            }
            if (!CurrencyCode.TryParse(item.CustomsValueCurrency, out var currency))
            {
                throw new InternationalShipmentValidationException($"'{item.CustomsValueCurrency}' is not a valid three-letter currency code.");
            }
            items.Add(new ValidatedCustomsItem(item.ShipmentItemId, hsCode, item.CustomsValueAmount, currency.Value));
        }
        if (!shipmentItemIds.SetEquals(seen))
        {
            throw new InternationalShipmentValidationException("The declaration must include exactly one customs item for every shipment item.");
        }

        return new ValidatedCustoms(countryOfOrigin.ToUpperInvariant(), purpose, incoterm, items);
    }

    // Snapshot of the whole declaration for the audit log's Before/After (CLAUDE.md §28).
    private static string SerializeDeclaration(InternationalLeg leg, IEnumerable<CustomsItem> items) => JsonSerializer.Serialize(new
    {
        leg.ContentsDescription,
        leg.CountryOfOrigin,
        Purpose = leg.Purpose.ToString(),
        Incoterm = leg.Incoterm.ToString(),
        leg.ExportLicenceReference,
        Items = items.OrderBy(i => i.ShipmentItemId).Select(i => new { i.ShipmentItemId, i.HsCode, i.CustomsValueAmount, i.CustomsValueCurrency })
    });
}
