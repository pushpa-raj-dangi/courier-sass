using System.Security.Claims;
using Courier.Audit.Application;
using Courier.SharedKernel;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService : IShipmentInternationalGateway
{
    public async Task<ShipmentResponse> CreateShipmentShellAsync(CreateInternationalShipmentShellRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        // Defense in depth — International.Application validates Direction up-front too, but the
        // gateway must never create a Scope = International shipment with Direction = Domestic (or
        // an undefined numeric value bound straight from JSON).
        if (request.Direction is not (ShipmentDirection.Outbound or ShipmentDirection.Inbound))
        {
            throw new ShipmentValidationException("Direction must be Outbound or Inbound for an international shipment.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ShipmentValidationException("A shipment must have at least one item.");
        }

        // Same tenant checks domestic CreateShipmentAsync performs (merchant via
        // IMerchantLookupService.ExistsAsync; branches via IBranchLookupService.GetZoneInfoAsync,
        // which domestic reaches through PricingService.QuoteAsync). Shipment's FKs to
        // Merchant/Branch are single-column, so without these a cross-tenant id would persist.
        if (!await _merchantLookupService.ExistsAsync(request.MerchantId, cancellationToken))
        {
            throw new ShipmentValidationException("Merchant was not found in this organization.");
        }
        if (await _branchLookupService.GetZoneInfoAsync(request.OriginBranchId, cancellationToken) is null)
        {
            throw new ShipmentValidationException("Origin branch was not found in this organization.");
        }
        if (await _branchLookupService.GetZoneInfoAsync(request.DestinationBranchId, cancellationToken) is null)
        {
            throw new ShipmentValidationException("Destination branch was not found in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "international.create", request.OriginBranchId, cancellationToken);

        // The Nepal-side party is addressed by its geography chain only — any InternationalAddress
        // fields sent for it are dropped rather than persisted unvalidated.
        var recipientIsForeign = request.Direction == ShipmentDirection.Outbound;

        var declaredValue = request.Items.Sum(i => i.Quantity * i.UnitValue);

        const int maxAwbAttempts = 8;
        Shipment shipment = null!;
        for (var attempt = 1; attempt <= maxAwbAttempts; attempt++)
        {
            var awb = await GenerateAwbAsync(organizationId, cancellationToken);

            shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                OrganizationId = organizationId,
                Awb = awb,
                MerchantId = request.MerchantId,
                OriginBranchId = request.OriginBranchId,
                DestinationBranchId = request.DestinationBranchId,
                CurrentBranchId = request.OriginBranchId,
                CurrentStatus = ShipmentStatus.Created,
                ServiceTypeId = request.ServiceTypeId,
                Scope = ShipmentScope.International,
                Direction = request.Direction,
                WeightKg = request.WeightKg,
                LengthCm = request.LengthCm,
                WidthCm = request.WidthCm,
                HeightCm = request.HeightCm,
                DeclaredValue = declaredValue,
                CodAmount = 0m,
                WantsInsurance = false,
                DeliveryCharge = request.ProvisionalTotalChargeAmount,
                InsuranceCharge = 0m,
                OtherCharges = 0m,
                TotalCharge = request.ProvisionalTotalChargeAmount,
                RecipientName = request.RecipientName,
                RecipientPhone = request.RecipientPhone,
                RecipientProvinceId = request.RecipientProvinceId,
                RecipientDistrictId = request.RecipientDistrictId,
                RecipientLocalLevelId = request.RecipientLocalLevelId,
                RecipientWardNumber = request.RecipientWardNumber,
                RecipientAddressLine = request.RecipientAddressLine,
                RecipientCity = recipientIsForeign ? request.RecipientCity : null,
                RecipientRegion = recipientIsForeign ? request.RecipientRegion : null,
                RecipientPostalCode = recipientIsForeign ? request.RecipientPostalCode : null,
                RecipientCountryCode = recipientIsForeign ? request.RecipientCountryCode?.ToUpperInvariant() : null,
                SenderName = request.SenderName,
                SenderPhone = request.SenderPhone,
                SenderProvinceId = request.SenderProvinceId,
                SenderDistrictId = request.SenderDistrictId,
                SenderLocalLevelId = request.SenderLocalLevelId,
                SenderWardNumber = request.SenderWardNumber,
                SenderAddressLine = request.SenderAddressLine,
                SenderCity = recipientIsForeign ? null : request.SenderCity,
                SenderRegion = recipientIsForeign ? null : request.SenderRegion,
                SenderPostalCode = recipientIsForeign ? null : request.SenderPostalCode,
                SenderCountryCode = recipientIsForeign ? null : request.SenderCountryCode?.ToUpperInvariant()
            };

            await ShipmentAddressValidator.ValidateAsync(shipment, _geographyLookupService, cancellationToken);

            _dbContext.Set<Shipment>().Add(shipment);

            foreach (var item in request.Items)
            {
                _dbContext.Set<ShipmentItem>().Add(new ShipmentItem
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitValue = item.UnitValue
                });
            }

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.Created,
                PreviousStatus = null,
                NewStatus = ShipmentStatus.Created,
                BranchId = request.OriginBranchId,
                UserId = actingUser.GetUserId(),
                Timestamp = DateTimeOffset.UtcNow
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < maxAwbAttempts)
            {
                foreach (var entry in _dbContext.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "international_shipment.created", "Shipment", shipment.Id.ToString()), cancellationToken);
        await _outboxWriter.WriteAsync(organizationId, "shipment.created", new { ShipmentId = shipment.Id, MerchantId = request.MerchantId, Awb = shipment.Awb }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    public async Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (shipment.Scope != ShipmentScope.International)
        {
            throw new ShipmentValidationException("Only international shipments can transition to CustomsPrepared.");
        }

        // Same "the operator must be at the branch physically holding the parcel" rule as
        // ReceiveAtOriginAsync's scan check. A shipment with no CurrentBranchId (in transit / with a
        // rider) can never be at Sorted/AtDestinationBranch, so for a branch-restricted caller the
        // state guard below would reject it anyway; for them it fails here first.
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "international.manage_customs", cancellationToken);
        if (accessible is not null && (shipment.CurrentBranchId is not Guid currentBranchId || !accessible.Contains(currentBranchId)))
        {
            throw new ShipmentValidationException("Branch is outside your branch access for permission 'international.manage_customs'.");
        }

        var expectedFrom = shipment.OriginBranchId == shipment.DestinationBranchId
            ? ShipmentStatus.Sorted
            : ShipmentStatus.AtDestinationBranch;

        return await TransitionAsync(shipmentId, expectedFrom, ShipmentStatus.CustomsPrepared, ShipmentEventType.CustomsPrepared,
            _ => { }, "shipment.customs_prepared", actingUser, cancellationToken);
    }
}
