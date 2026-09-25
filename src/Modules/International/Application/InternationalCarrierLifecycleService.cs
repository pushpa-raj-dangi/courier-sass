using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Handoff / receive scans and staff overrides. Visibility (404) is checked here with the same
// origin/destination/current rule as every international route; the gateway then enforces the
// stricter "operator is at the right branch" rule (400) and the state machine.
public class InternationalCarrierLifecycleService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentInternationalGateway _gateway;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public InternationalCarrierLifecycleService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentInternationalGateway gateway, IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _gateway = gateway;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<ShipmentResponse?> HandoffAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var leg = await LoadVisibleLegAsync(shipmentId, actingUser, "international.handoff", cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var booking = await LiveBookedAsync(leg.Id, CarrierBookingDirection.Outbound, cancellationToken)
            ?? throw new InternationalShipmentValidationException("A booked outbound carrier booking is required before handoff.");

        // The shipment transition and the booking's custody start commit together.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }
        try
        {
            var result = await _gateway.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(booking.CarrierAccountId, booking.CarrierAwb!), actingUser, cancellationToken);
            if (result is null)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                return null;
            }
            booking.CustodyStartedAt = DateTimeOffset.UtcNow;
            booking.UpdatedAt = booking.CustodyStartedAt.Value;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.custody_started", "CarrierBooking", booking.Id.ToString()), cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
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

    public async Task<ShipmentResponse?> ReceiveAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        RequireOrganization();
        var leg = await LoadVisibleLegAsync(shipmentId, actingUser, "international.receive", cancellationToken);
        if (leg is null)
        {
            return null;
        }
        _ = await LiveBookedAsync(leg.Id, CarrierBookingDirection.Inbound, cancellationToken)
            ?? throw new InternationalShipmentValidationException("Link the inbound carrier booking before receiving the parcel.");
        return await _gateway.ReceiveFromCarrierAsync(shipmentId, actingUser, cancellationToken);
    }

    public async Task<ShipmentResponse?> OverrideStatusAsync(Guid shipmentId, StatusOverrideRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        RequireOrganization();
        var leg = await LoadVisibleLegAsync(shipmentId, actingUser, "international.tracking.manage", cancellationToken);
        if (leg is null)
        {
            return null;
        }
        return await _gateway.OverrideInternationalStatusAsync(shipmentId, request.TargetStatus, request.Reason ?? string.Empty, actingUser, cancellationToken);
    }

    private Guid RequireOrganization() =>
        _currentTenantProvider.OrganizationId ?? throw new InternationalShipmentValidationException("No active organization context.");

    private async Task<InternationalLeg?> LoadVisibleLegAsync(Guid shipmentId, ClaimsPrincipal actingUser, string permission, CancellationToken cancellationToken)
    {
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var scan = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scan is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, permission,
                scan.OriginBranchId, scan.DestinationBranchId, scan.CurrentBranchId, cancellationToken))
        {
            return null;
        }
        return leg;
    }

    private Task<CarrierBooking?> LiveBookedAsync(Guid legId, CarrierBookingDirection direction, CancellationToken cancellationToken) =>
        _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == legId && b.Status == CarrierBookingStatus.Booked && b.Direction == direction, cancellationToken);
}
