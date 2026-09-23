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

        if (request.Items.Count == 0)
        {
            throw new ShipmentValidationException("A shipment must have at least one item.");
        }

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
                RecipientCity = request.RecipientCity,
                RecipientRegion = request.RecipientRegion,
                RecipientPostalCode = request.RecipientPostalCode,
                RecipientCountryCode = request.RecipientCountryCode,
                SenderName = request.SenderName,
                SenderPhone = request.SenderPhone,
                SenderProvinceId = request.SenderProvinceId,
                SenderDistrictId = request.SenderDistrictId,
                SenderLocalLevelId = request.SenderLocalLevelId,
                SenderWardNumber = request.SenderWardNumber,
                SenderAddressLine = request.SenderAddressLine,
                SenderCity = request.SenderCity,
                SenderRegion = request.SenderRegion,
                SenderPostalCode = request.SenderPostalCode,
                SenderCountryCode = request.SenderCountryCode
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

        var expectedFrom = shipment.OriginBranchId == shipment.DestinationBranchId
            ? ShipmentStatus.Sorted
            : ShipmentStatus.AtDestinationBranch;

        return await TransitionAsync(shipmentId, expectedFrom, ShipmentStatus.CustomsPrepared, ShipmentEventType.CustomsPrepared,
            _ => { }, "shipment.customs_prepared", actingUser, cancellationToken);
    }
}
