using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

// This plan's resolved design decision: RiderDelivery is NOT a second call after assignment the
// way RiderPickup is (assign -> PICKUP_ASSIGNED, then a separate pickup scan -> PICKED_UP).
// SHIPMENT-LIFECYCLE.md's transition table has exactly one arrow into OUT_FOR_DELIVERY ("delivery
// rider assignment + out-for-delivery scan"), matching Phase 6's AssignDeliveryAsync having no
// sibling scan endpoint at all. So AssignDeliveryToRiderAsync below IS the RiderDelivery scan —
// assigning a delivery task to a rider and scanning it out for delivery are the same action.
// Pickup keeps its two-step shape (an explicit PICKUP_ASSIGNED status exists for dispatch boards
// to show "assigned but not yet physically collected"); delivery has no equivalent named status
// between "assigned" and "out for delivery", so there's nothing for a second step to represent.
public partial class ShipmentService
{
    private async Task<ShipmentResponse?> TransitionAsync(
        Guid shipmentId, ShipmentStatus expectedFrom, ShipmentStatus to, ShipmentEventType eventType,
        Action<Shipment> applyCustody, string auditAction, ClaimsPrincipal actingUser, CancellationToken cancellationToken,
        string? notes = null, Guid? riderId = null)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        if (shipment.CurrentStatus != expectedFrom)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, to);
        }

        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = shipment.CurrentBranchId;
        shipment.CurrentStatus = to;
        applyCustody(shipment);

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = to,
            BranchId = eventBranchId,
            UserId = actingUser.GetUserId(),
            RiderId = riderId,
            Timestamp = DateTimeOffset.UtcNow,
            Notes = notes
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another concurrent request already moved this shipment out from under us (the xmin
            // row-version token no longer matches) — semantically this IS an invalid-transition
            // attempt from this caller's point of view, so it maps onto the same, already-wired
            // SHIPMENT_INVALID_TRANSITION error shape (CLAUDE.md §11/§39: two racing identical
            // transition requests must not both succeed and double-append ShipmentEvent rows).
            throw new ShipmentInvalidTransitionException(shipmentId, expectedFrom, to);
        }
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), auditAction, "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ShipmentValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    // Callers of every method below are Riders.Application.RiderService, which performs
    // BRANCH-OPERATIONS.md §1 checks 1 (permission), 2 (branch access), 4 (expected operation), and
    // 5 (custody consistency) itself before ever reaching here — exactly the same division of
    // responsibility Phase 7 established between ManifestService and these Sort/Receive methods.
    // Only check 3 (shipment state, via TransitionAsync's expectedFrom guard) happens here.

    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.Created, ShipmentStatus.PickupAssigned, ShipmentEventType.PickupAssigned,
            s => s.CurrentCustodianRiderId = riderId, "shipment.pickup_assigned", actingUser, cancellationToken, riderId: riderId);

    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.PickupAssigned, ShipmentStatus.PickedUp, ShipmentEventType.PickedUp,
            _ => { }, "shipment.picked_up", actingUser, cancellationToken, riderId: riderId);

    public async Task<ShipmentResponse?> ReceiveAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.scan", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.PickedUp, ShipmentStatus.AtOriginBranch, ShipmentEventType.ReceivedAtOrigin,
            s => { s.CurrentBranchId = s.OriginBranchId; s.CurrentCustodianUserId = null; s.CurrentCustodianRiderId = null; }, "shipment.received_at_origin", actingUser, cancellationToken);
    }

    // Branch-scope/permission enforcement for this operation lives one layer up in
    // Manifests.Application.ManifestService.ScanAsync (BRANCH-OPERATIONS.md §1 checks 1/2), which
    // is the only caller of this method as of Phase 7.
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.AtOriginBranch, ShipmentStatus.Sorted, ShipmentEventType.Sorted,
            _ => { }, "shipment.sorted", actingUser, cancellationToken);

    // Used exclusively by Manifests.Application.ManifestService.DispatchAsync — see Phase 7's
    // reasoning in this same file's history for why this stages N shipments into one SaveChangesAsync.
    public async Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipments = await _dbContext.Set<Shipment>().Where(s => shipmentIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var actingUserId = actingUser.GetUserId();
        var now = DateTimeOffset.UtcNow;

        foreach (var shipment in shipments)
        {
            if (shipment.CurrentStatus != ShipmentStatus.Sorted)
            {
                throw new ShipmentInvalidTransitionException(shipment.Id, shipment.CurrentStatus, ShipmentStatus.InTransit);
            }
            var previousStatus = shipment.CurrentStatus;
            var eventBranchId = shipment.CurrentBranchId;
            shipment.CurrentStatus = ShipmentStatus.InTransit;
            shipment.CurrentBranchId = null;

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.Dispatched,
                PreviousStatus = previousStatus,
                NewStatus = ShipmentStatus.InTransit,
                BranchId = eventBranchId,
                UserId = actingUserId,
                Timestamp = now
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentValidationException("One or more shipments on this manifest were modified concurrently; dispatch was not applied.");
        }
    }

    // Same reasoning as SortAsync above: Manifests.Application.ManifestService.ReceiveScanAsync is
    // the only caller as of Phase 7.
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.InTransit, ShipmentStatus.AtDestinationBranch, ShipmentEventType.ReceivedAtDestination,
            s => s.CurrentBranchId = s.DestinationBranchId, "shipment.received_at_destination", actingUser, cancellationToken);

    // IS the RiderDelivery scan — see this file's top-of-namespace comment for why there's no
    // separate second "delivery scan" step the way pickup has one.
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.AtDestinationBranch, ShipmentStatus.OutForDelivery, ShipmentEventType.OutForDelivery,
            s => { s.CurrentCustodianRiderId = riderId; s.CurrentBranchId = null; }, "shipment.out_for_delivery", actingUser, cancellationToken, riderId: riderId);

    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentEventType.Delivered,
            s => s.CurrentCustodianRiderId = null, "shipment.delivered", actingUser, cancellationToken,
            notes: $"Delivered to: {recipientName}.", riderId: riderId);

    // Custody deliberately stays with the rider (CurrentCustodianRiderId is NOT cleared) — the
    // parcel is still in the rider's physical possession after a failed attempt, it hasn't gone
    // back to a branch. Phase 9's NDR/reattempt/RTO flow is what eventually moves custody again.
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.OutForDelivery, ShipmentStatus.DeliveryFailed, ShipmentEventType.DeliveryFailed,
            _ => { }, "shipment.delivery_failed", actingUser, cancellationToken, riderId: riderId);

    public async Task<ShipmentResponse?> CancelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && shipment.MerchantId != callerMerchantId)
        {
            return null;
        }

        if (shipment.CurrentStatus != ShipmentStatus.Created && shipment.CurrentStatus != ShipmentStatus.PickupAssigned)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, ShipmentStatus.Cancelled);
        }

        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = shipment.CurrentBranchId;
        shipment.CurrentStatus = ShipmentStatus.Cancelled;
        shipment.CurrentCustodianUserId = null;
        shipment.CurrentCustodianRiderId = null;
        shipment.CurrentBranchId = null;

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = ShipmentEventType.Cancelled,
            PreviousStatus = previousStatus,
            NewStatus = ShipmentStatus.Cancelled,
            BranchId = eventBranchId,
            UserId = actingUser.GetUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Same race-handling shape as TransitionAsync above: another concurrent request
            // already mutated this shipment out from under us.
            throw new ShipmentInvalidTransitionException(shipmentId, previousStatus, ShipmentStatus.Cancelled);
        }
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.cancelled", "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }
}
