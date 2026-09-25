using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<InternationalCarrierLifecycleService> _logger;

    public InternationalCarrierLifecycleService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentInternationalGateway gateway, IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker,
        ILogger<InternationalCarrierLifecycleService> logger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _gateway = gateway;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _logger = logger;
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
                await RollbackQuietlyAsync(transaction, shipmentId, originalException: null);
                return null;
            }

            // Fix round 1: a concurrent CancelAsync could have cancelled this exact booking between
            // our read above and now — never stamp custody onto a booking that is no longer Booked.
            var custodyStartedAt = DateTimeOffset.UtcNow;
            if (_dbContext.Database.IsRelational())
            {
                var affected = await _dbContext.Set<CarrierBooking>()
                    .Where(b => b.Id == booking.Id && b.Status == CarrierBookingStatus.Booked)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.CustodyStartedAt, custodyStartedAt)
                        .SetProperty(b => b.UpdatedAt, custodyStartedAt), cancellationToken);
                if (affected == 0)
                {
                    throw new InternationalShipmentValidationException("The carrier booking is no longer booked; handoff was not recorded.");
                }
                // The raw ExecuteUpdate above bypassed the tracker — accept these exact values as
                // the tracker's new baseline (same technique as CarrierBookingService.AcceptTrackedChanges).
                booking.CustodyStartedAt = custodyStartedAt;
                booking.UpdatedAt = custodyStartedAt;
                AcceptTrackedChanges(booking);
            }
            else
            {
                // EF Core's InMemory provider (unit tests) cannot run ExecuteUpdate — an equivalent
                // tracked compare-and-set: re-read fresh (untracked) to check the currently-committed
                // status, then save through the already-tracked `booking` only if it still matches.
                var fresh = await _dbContext.Set<CarrierBooking>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id, CancellationToken.None);
                if (fresh is null || fresh.Status != CarrierBookingStatus.Booked)
                {
                    throw new InternationalShipmentValidationException("The carrier booking is no longer booked; handoff was not recorded.");
                }
                booking.CustodyStartedAt = custodyStartedAt;
                booking.UpdatedAt = custodyStartedAt;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.custody_started", "CarrierBooking", booking.Id.ToString()), cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        catch (Exception ex)
        {
            await RollbackQuietlyAsync(transaction, shipmentId, ex);
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

    // Fix round 1: a failed rollback must never mask the original exception — log it and always
    // rethrow (or return, for the not-found path) what the caller actually raised.
    private async Task RollbackQuietlyAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction, Guid shipmentId, Exception? originalException)
    {
        if (transaction is null)
        {
            return;
        }
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "Failed to roll back the handoff transaction for shipment {ShipmentId}{Reason}.",
                shipmentId, originalException is null ? string.Empty : $" after: {originalException.Message}");
        }
    }

    // Accepts an entity's CURRENT (already-mutated) in-memory values as the change tracker's new
    // baseline, after those exact values were just committed via a raw ExecuteUpdate that bypassed
    // the tracker entirely — same technique (and same reason) as CarrierBookingService's private
    // AcceptTrackedChanges. Not shared directly: that helper is private to CarrierBookingService and
    // widening its visibility for one caller elsewhere isn't warranted.
    private void AcceptTrackedChanges(CarrierBooking entity)
    {
        var entry = _dbContext.Entry(entity);
        entry.OriginalValues.SetValues(entry.CurrentValues);
        entry.State = EntityState.Unchanged;
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
