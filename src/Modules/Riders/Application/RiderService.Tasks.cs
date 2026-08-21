using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    public async Task<DeliveryTaskResponse> AssignTaskAsync(AssignDeliveryTaskRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");

        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == request.RiderId, cancellationToken)
            ?? throw new RiderNotFoundException(request.RiderId);
        if (!rider.IsActive)
        {
            throw new RiderValidationException("Rider is not active.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(request.ShipmentId, cancellationToken)
            ?? throw new RiderValidationException("Shipment was not found.");

        // Pickup tasks are assigned/branch-scoped against the shipment's origin (the rider must be
        // able to operate at the branch it's leaving from); delivery tasks against the destination
        // (where the rider must physically be to take it out for delivery).
        var branchId = request.Type == DeliveryTaskType.Pickup ? scanInfo.OriginBranchId : scanInfo.DestinationBranchId;
        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.assign");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.assign", branchId, cancellationToken);

        // Checks 3 (state) + 5 (custody) — check 4 (expected operation) collapses into check 3 here,
        // same reasoning as ManifestService.ScanAsync (Phase 7): the only thing this endpoint does
        // for each task type IS that exact transition.
        if (request.Type == DeliveryTaskType.Pickup)
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.Created)
            {
                throw new RiderValidationException($"Shipment must be Created to assign a pickup task, was {scanInfo.CurrentStatus}.");
            }
        }
        else
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.AtDestinationBranch)
            {
                throw new RiderValidationException($"Shipment must be AtDestinationBranch to assign a delivery task, was {scanInfo.CurrentStatus}.");
            }
            if (scanInfo.CurrentBranchId != branchId)
            {
                throw new RiderValidationException("Shipment's current branch does not match this rider's base branch.");
            }
        }

        // Perform the real transition FIRST — only persist DeliveryRun/DeliveryTask bookkeeping once
        // it succeeds, mirroring ManifestService.ScanAsync's "transition, then record" ordering so a
        // failed transition never leaves an orphan task row behind.
        if (request.Type == DeliveryTaskType.Pickup)
        {
            await _shipmentTransitionService.AssignPickupToRiderAsync(request.ShipmentId, rider.Id, actingUser, cancellationToken);
        }
        else
        {
            await _shipmentTransitionService.AssignDeliveryToRiderAsync(request.ShipmentId, rider.Id, actingUser, cancellationToken);
        }

        // Find-or-create today's Active run for (RiderId, BranchId) — this plan's resolved design
        // decision: no explicit "start my run" endpoint.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var run = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(
            r => r.RiderId == rider.Id && r.BranchId == branchId && r.Date == today && r.Status == DeliveryRunStatus.Active, cancellationToken);
        if (run is null)
        {
            run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = today, Status = DeliveryRunStatus.Active };
            _dbContext.Set<DeliveryRun>().Add(run);
        }

        var maxSequence = await _dbContext.Set<DeliveryTask>()
            .Where(t => t.DeliveryRunId == run.Id)
            .Select(t => (int?)t.Sequence)
            .MaxAsync(cancellationToken);

        var task = new DeliveryTask
        {
            Id = Guid.NewGuid(),
            DeliveryRunId = run.Id,
            ShipmentId = request.ShipmentId,
            Type = request.Type,
            Sequence = (maxSequence ?? 0) + 1,
            Status = DeliveryTaskStatus.Pending
        };
        _dbContext.Set<DeliveryTask>().Add(task);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "delivery_task.assigned", "DeliveryTask", task.Id.ToString()), cancellationToken);
        return ToTaskResponse(task);
    }

    public async Task<ShipmentResponse> RiderPickupScanAsync(Guid riderId, RiderScanRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new RiderValidationException($"No shipment found for AWB '{request.Awb}'.");

        // Check 1 (permission) enforced at the route via .RequirePermission("shipment.scan"); check
        // 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "shipment.scan", scanInfo.OriginBranchId, cancellationToken);

        // Checks 3+4 collapse: the only operation this endpoint performs IS the pickup scan.
        if (scanInfo.CurrentStatus != ShipmentStatus.PickupAssigned)
        {
            throw new RiderValidationException($"Shipment must be PickupAssigned to scan for pickup, was {scanInfo.CurrentStatus}.");
        }
        // Check 5 (custody consistency): must be assigned to THIS rider, not just any rider.
        if (scanInfo.CurrentCustodianRiderId != riderId)
        {
            throw new RiderValidationException("Shipment is not assigned to this rider.");
        }

        var result = await _shipmentTransitionService.RiderPickupScanAsync(scanInfo.ShipmentId, riderId, actingUser, cancellationToken);

        var task = await _dbContext.Set<DeliveryTask>()
            .FirstOrDefaultAsync(t => t.ShipmentId == scanInfo.ShipmentId && t.Type == DeliveryTaskType.Pickup && t.Status == DeliveryTaskStatus.Pending, cancellationToken);
        if (task is not null)
        {
            task.Status = DeliveryTaskStatus.Completed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(scanInfo.OrganizationId, actingUser.GetUserId(), "rider.pickup_scanned", "Shipment", scanInfo.ShipmentId.ToString()), cancellationToken);
        return result!;
    }

    private static DeliveryTaskResponse ToTaskResponse(DeliveryTask task) => new(
        task.Id, task.DeliveryRunId, task.ShipmentId, task.Type, task.Sequence, task.Status);
}
