using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    public async Task<DeliveryAttemptResponse> RecordFailedAttemptAsync(Guid riderId, RecordFailedAttemptRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(request.ShipmentId, cancellationToken)
            ?? throw new RiderValidationException("Shipment was not found.");

        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.attempt");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.attempt", scanInfo.DestinationBranchId, cancellationToken);

        if (scanInfo.CurrentStatus != ShipmentStatus.OutForDelivery)
        {
            throw new RiderValidationException($"Shipment must be OutForDelivery to record a delivery attempt, was {scanInfo.CurrentStatus}.");
        }
        if (scanInfo.CurrentCustodianRiderId != riderId)
        {
            throw new RiderValidationException("Shipment is not assigned to this rider.");
        }

        var attemptNumber = await _dbContext.Set<DeliveryAttempt>().CountAsync(a => a.ShipmentId == request.ShipmentId, cancellationToken) + 1;

        // Transition first, then persist the attempt record — same "transition, then record"
        // ordering as AssignTaskAsync (Task 8).
        await _shipmentTransitionService.FailDeliveryAsync(request.ShipmentId, riderId, actingUser, cancellationToken);

        var attempt = new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = request.ShipmentId,
            RiderId = riderId,
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTimeOffset.UtcNow,
            Succeeded = false,
            Reason = request.Reason,
            Notes = request.Notes,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CustomerContacted = request.CustomerContacted,
            NextAction = request.NextAction
        };
        _dbContext.Set<DeliveryAttempt>().Add(attempt);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var task = await _dbContext.Set<DeliveryTask>()
            .FirstOrDefaultAsync(t => t.ShipmentId == request.ShipmentId && t.Type == DeliveryTaskType.Delivery && t.Status == DeliveryTaskStatus.Pending, cancellationToken);
        if (task is not null)
        {
            task.Status = DeliveryTaskStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "delivery.attempt_failed", "DeliveryAttempt", attempt.Id.ToString()), cancellationToken);
        return ToAttemptResponse(attempt);
    }

    public async Task<UploadPodFileResponse> UploadPodFileAsync(Guid riderId, PodFileKind kind, Stream content, string contentType, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var storageKey = await _objectStorage.UploadAsync($"pod/{kind}/{riderId}", content, contentType, cancellationToken);
        return new UploadPodFileResponse(storageKey);
    }

    public async Task<DeliveryAttemptResponse> CompleteDeliveryAsync(Guid riderId, CompleteDeliveryRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(request.ShipmentId, cancellationToken)
            ?? throw new RiderValidationException("Shipment was not found.");

        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.complete");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.complete", scanInfo.DestinationBranchId, cancellationToken);

        if (scanInfo.CurrentStatus != ShipmentStatus.OutForDelivery)
        {
            throw new RiderValidationException($"Shipment must be OutForDelivery to complete delivery, was {scanInfo.CurrentStatus}.");
        }
        if (scanInfo.CurrentCustodianRiderId != riderId)
        {
            throw new RiderValidationException("Shipment is not assigned to this rider.");
        }

        var attemptNumber = await _dbContext.Set<DeliveryAttempt>().CountAsync(a => a.ShipmentId == request.ShipmentId, cancellationToken) + 1;

        await _shipmentTransitionService.CompleteDeliveryAsync(request.ShipmentId, riderId, request.RecipientName, actingUser, cancellationToken);

        var attempt = new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = request.ShipmentId,
            RiderId = riderId,
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTimeOffset.UtcNow,
            Succeeded = true,
            Reason = null,
            CustomerContacted = true
        };
        _dbContext.Set<DeliveryAttempt>().Add(attempt);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var proof = new DeliveryProof
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = request.ShipmentId,
            DeliveryAttemptId = attempt.Id,
            RiderId = riderId,
            OtpVerified = request.OtpVerified,
            SignatureStorageKey = request.SignatureStorageKey,
            PhotoStorageKey = request.PhotoStorageKey,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Timestamp = DateTimeOffset.UtcNow,
            RecipientName = request.RecipientName
        };
        _dbContext.Set<DeliveryProof>().Add(proof);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var task = await _dbContext.Set<DeliveryTask>()
            .FirstOrDefaultAsync(t => t.ShipmentId == request.ShipmentId && t.Type == DeliveryTaskType.Delivery && t.Status == DeliveryTaskStatus.Pending, cancellationToken);
        if (task is not null)
        {
            task.Status = DeliveryTaskStatus.Completed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "delivery.completed", "DeliveryProof", proof.Id.ToString()), cancellationToken);
        return ToAttemptResponse(attempt);
    }

    private static DeliveryAttemptResponse ToAttemptResponse(DeliveryAttempt attempt) => new(
        attempt.Id, attempt.ShipmentId, attempt.RiderId, attempt.AttemptNumber, attempt.Succeeded, attempt.Reason, attempt.NextAction);
}
