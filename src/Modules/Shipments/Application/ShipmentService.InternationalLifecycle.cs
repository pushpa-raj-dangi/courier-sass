using System.Security.Claims;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.SharedKernel;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

// I4 international carrier lifecycle. Unlike TransitionAsync (one fixed expectedFrom, always a
// user, always "now"), these moves are validated against InternationalTransitionRules, may have
// no user (carrier-driven), carry the carrier's own timestamp, and a scan may record two events in
// one save (inbound receive at the destination branch). Same xmin concurrency handling.
public partial class ShipmentService
{
    private const int MaxOverrideReasonLength = 2000; // ShipmentEvent.Notes column limit

    public async Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return CarrierStatusApplyResult.NotFound;
        }
        if (shipment.Scope != ShipmentScope.International)
        {
            throw new ShipmentValidationException("Carrier status updates apply only to international shipments.");
        }
        if (shipment.CurrentStatus == targetStatus)
        {
            return CarrierStatusApplyResult.AlreadyAtStatus;
        }
        if (!InternationalTransitionRules.IsAllowed(shipment.Direction, shipment.CurrentStatus, targetStatus, InternationalTransitionTrigger.Carrier))
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, targetStatus);
        }

        var previousStatus = shipment.CurrentStatus;
        var metadata = JsonSerializer.Serialize(new
        {
            source = "Carrier",
            carrierAccountId = reference.CarrierAccountId,
            carrierAwb = reference.CarrierAwb,
            carrierEventId = reference.CarrierEventId,
            carrierStatusCode = reference.CarrierStatusCode,
            location = reference.Location
        });
        StageInternationalTransition(shipment, targetStatus, InternationalTransitionRules.EventTypeFor(targetStatus),
            userId: null, occurredAt, notes: null, metadata, _ => { });
        await SaveInternationalTransitionAsync(shipmentId, previousStatus, targetStatus, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "shipment.carrier_status_applied", "Shipment", shipmentId.ToString(),
            Before: previousStatus.ToString(), After: targetStatus.ToString()), cancellationToken);
        return CarrierStatusApplyResult.Applied;
    }

    public async Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
        {
            throw new ShipmentValidationException("A reason is required for a status override.");
        }
        if (trimmedReason.Length > MaxOverrideReasonLength)
        {
            throw new ShipmentValidationException($"Reason must be at most {MaxOverrideReasonLength} characters.");
        }

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (shipment.Scope != ShipmentScope.International)
        {
            throw new ShipmentValidationException("Status overrides apply only to international shipments.");
        }
        if (!InternationalTransitionRules.IsAllowed(shipment.Direction, shipment.CurrentStatus, targetStatus, InternationalTransitionTrigger.StaffOverride))
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, targetStatus);
        }

        var previousStatus = shipment.CurrentStatus;
        StageInternationalTransition(shipment, targetStatus, InternationalTransitionRules.EventTypeFor(targetStatus),
            actingUser.GetUserId(), DateTimeOffset.UtcNow, trimmedReason, JsonSerializer.Serialize(new { source = "StaffOverride" }), _ => { });
        await SaveInternationalTransitionAsync(shipmentId, previousStatus, targetStatus, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.international_status_overridden", "Shipment", shipmentId.ToString(),
            Before: previousStatus.ToString(), After: JsonSerializer.Serialize(new { status = targetStatus.ToString(), reason = trimmedReason })), cancellationToken);
        return await LoadResponseAsync(shipmentId, cancellationToken);
    }

    // Mutates the tracked shipment and adds its immutable ShipmentEvent; nothing is saved here so
    // a scan can stage two moves (receive + arrive) into one SaveChangesAsync.
    private void StageInternationalTransition(
        Shipment shipment, ShipmentStatus to, ShipmentEventType eventType, Guid? userId, DateTimeOffset timestamp,
        string? notes, string? metadata, Action<Shipment> applyCustody, Guid? eventBranchIdOverride = null)
    {
        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = eventBranchIdOverride ?? shipment.CurrentBranchId;
        shipment.CurrentStatus = to;
        applyCustody(shipment);

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = to,
            BranchId = eventBranchId,
            UserId = userId,
            Timestamp = timestamp,
            Notes = notes,
            Metadata = metadata
        });
    }

    private async Task SaveInternationalTransitionAsync(Guid shipmentId, ShipmentStatus from, ShipmentStatus to, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Same race handling as TransitionAsync: another request moved the shipment first.
            throw new ShipmentInvalidTransitionException(shipmentId, from, to);
        }
    }
}
