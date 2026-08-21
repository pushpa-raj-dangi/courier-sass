using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    private async Task<ShipmentResponse?> TransitionAsync(
        Guid shipmentId, ShipmentStatus expectedFrom, ShipmentStatus to, ShipmentEventType eventType,
        Action<Shipment> applyCustody, string auditAction, ClaimsPrincipal actingUser, CancellationToken cancellationToken,
        string? notes = null)
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

    public Task<ShipmentResponse?> AssignPickupAsync(Guid shipmentId, AssignPickupRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.Created, ShipmentStatus.PickupAssigned, ShipmentEventType.PickupAssigned,
            s => s.CurrentCustodianUserId = request.AssignedUserId, "shipment.pickup_assigned", actingUser, cancellationToken);

    public async Task<ShipmentResponse?> PickupScanAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.scan", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.PickupAssigned, ShipmentStatus.PickedUp, ShipmentEventType.PickedUp,
            _ => { }, "shipment.picked_up", actingUser, cancellationToken);
    }

    public async Task<ShipmentResponse?> ReceiveAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.scan", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.PickedUp, ShipmentStatus.AtOriginBranch, ShipmentEventType.ReceivedAtOrigin,
            s => { s.CurrentBranchId = s.OriginBranchId; s.CurrentCustodianUserId = null; }, "shipment.received_at_origin", actingUser, cancellationToken);
    }

    // Branch-scope/permission enforcement for this operation now lives one layer up in
    // Manifests.Application.ManifestService.ScanAsync (BRANCH-OPERATIONS.md §1 checks 1/2), which
    // is the only caller of this method as of Phase 7 — its own manifest.create permission and
    // FromBranchId branch check happen before this is ever reached, so re-checking
    // "shipment.sort"/branch access here would be redundant and would wrongly require callers to
    // hold a permission this phase's design deliberately retired for this operation.
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.AtOriginBranch, ShipmentStatus.Sorted, ShipmentEventType.Sorted,
            _ => { }, "shipment.sorted", actingUser, cancellationToken);

    // Used exclusively by Manifests.Application.ManifestService.DispatchAsync to move every
    // shipment on a manifest Sorted -> InTransit as one unit. Deliberately does NOT loop calling
    // the single-shipment TransitionAsync helper (which SaveChangesAsync's per call) — that would
    // commit N separate transactions for one manifest-level dispatch action. Instead every
    // shipment's mutation + ShipmentEvent is staged in-memory and committed in exactly one
    // SaveChangesAsync, which EF Core already wraps in a single implicit database transaction on a
    // relational provider — true atomicity without introducing an explicit BeginTransactionAsync
    // (which the in-memory provider this module's unit tests use does not support).
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
    // the only caller as of Phase 7, and it enforces manifest.receive + ToBranchId branch access
    // itself before calling this.
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.InTransit, ShipmentStatus.AtDestinationBranch, ShipmentEventType.ReceivedAtDestination,
            s => s.CurrentBranchId = s.DestinationBranchId, "shipment.received_at_destination", actingUser, cancellationToken);

    public async Task<ShipmentResponse?> AssignDeliveryAsync(Guid shipmentId, AssignDeliveryRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "delivery.assign", shipment.DestinationBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.AtDestinationBranch, ShipmentStatus.OutForDelivery, ShipmentEventType.OutForDelivery,
            s => { s.CurrentCustodianUserId = request.AssignedUserId; s.CurrentBranchId = null; }, "shipment.out_for_delivery", actingUser, cancellationToken);
    }

    public async Task<ShipmentResponse?> DeliverAsync(Guid shipmentId, DeliverShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "delivery.complete", shipment.DestinationBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentEventType.Delivered,
            s => s.CurrentCustodianUserId = null, "shipment.delivered", actingUser, cancellationToken,
            notes: $"Delivered to: {request.RecipientName}.{(string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : " " + request.Notes)}");
    }

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
