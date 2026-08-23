using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Manifests.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestResponse> DispatchAsync(Guid manifestId, DispatchManifestRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.dispatch", manifest.FromBranchId, cancellationToken);

        if (manifest.Status != ManifestStatus.Sealed)
        {
            throw new ManifestValidationException($"Manifest must be Sealed to dispatch, was {manifest.Status}.");
        }
        if (string.IsNullOrWhiteSpace(request.VehiclePlate))
        {
            throw new ManifestValidationException("VehiclePlate is required to dispatch a manifest.");
        }

        var shipmentIds = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId)
            .Select(ms => ms.ShipmentId)
            .ToListAsync(cancellationToken);

        if (manifest.Purpose == ManifestPurpose.Forward)
        {
            // This plan's resolved design decision #2 (from Phase 7, still true): goes straight to
            // InTransit, no separate persisted "Dispatched" resting state.
            await _shipmentTransitionService.DispatchManyAsync(shipmentIds, actingUser, cancellationToken);
        }
        else
        {
            await _shipmentTransitionService.DispatchManyRtoAsync(shipmentIds, actingUser, cancellationToken);
        }

        manifest.VehiclePlate = request.VehiclePlate;
        manifest.DriverId = request.DriverId;
        manifest.Status = ManifestStatus.InTransit;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.dispatched", "Manifest", manifestId.ToString()), cancellationToken);
        return ToResponse(manifest);
    }

    public async Task<ManifestShipmentResponse> ReceiveScanAsync(Guid manifestId, ScanShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");
        if (string.IsNullOrWhiteSpace(request.Awb))
        {
            throw new ManifestValidationException("Awb is required.");
        }

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.receive", manifest.ToBranchId, cancellationToken);

        if (manifest.Status is not (ManifestStatus.InTransit or ManifestStatus.Received))
        {
            throw new ManifestScanRejectedException("MANIFEST_NOT_RECEIVING", $"Manifest is {manifest.Status}, not open for receiving.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new ManifestScanRejectedException("SHIPMENT_NOT_FOUND", $"No shipment found for AWB '{request.Awb}'.");

        // Check 4 (expected operation): the shipment must actually be listed on THIS manifest, not
        // just any in-transit shipment.
        var manifestShipment = await _dbContext.Set<ManifestShipment>()
            .FirstOrDefaultAsync(ms => ms.ManifestId == manifestId && ms.ShipmentId == scanInfo.ShipmentId, cancellationToken)
            ?? throw new ManifestScanRejectedException("NOT_ON_MANIFEST", "This shipment is not listed on this manifest.");

        if (manifestShipment.ReceivedAt is not null)
        {
            throw new ManifestScanRejectedException("ALREADY_RECEIVED", "This shipment has already been receive-scanned on this manifest.");
        }
        if (manifest.Purpose == ManifestPurpose.Forward)
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.InTransit)
            {
                throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be InTransit to receive, was {scanInfo.CurrentStatus}.");
            }
            // Check 5 (custody consistency): an in-transit shipment has no current branch, and its
            // declared destination must match where this manifest is actually headed.
            if (scanInfo.CurrentBranchId is not null)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's custody is inconsistent with being in transit.");
            }
            if (scanInfo.DestinationBranchId != manifest.ToBranchId)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's destination branch does not match this manifest's ToBranchId.");
            }

            await _shipmentTransitionService.ReceiveAtDestinationAsync(scanInfo.ShipmentId, actingUser, cancellationToken);
        }
        else
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.RtoInTransit)
            {
                throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be RtoInTransit to receive, was {scanInfo.CurrentStatus}.");
            }
            if (scanInfo.CurrentBranchId is not null)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's custody is inconsistent with being in transit.");
            }
            // For a return leg, ShipmentScanInfo.OriginBranchId still names the ORIGINAL shipment's
            // origin branch — that's exactly where this return manifest is headed (manifest.ToBranchId).
            if (scanInfo.OriginBranchId != manifest.ToBranchId)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's original origin branch does not match this return manifest's ToBranchId.");
            }

            await _shipmentTransitionService.ReceiveRtoAtOriginAsync(scanInfo.ShipmentId, actingUser, cancellationToken);
        }

        manifestShipment.ReceivedAt = DateTimeOffset.UtcNow;
        if (manifest.Status == ManifestStatus.InTransit)
        {
            manifest.Status = ManifestStatus.Received;
        }
        _dbContext.Set<ShipmentTransfer>().Add(new ShipmentTransfer
        {
            Id = Guid.NewGuid(),
            ManifestId = manifestId,
            ShipmentId = scanInfo.ShipmentId,
            FromBranchId = manifest.FromBranchId,
            ToBranchId = manifest.ToBranchId,
            Timestamp = manifestShipment.ReceivedAt.Value
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.shipment_received", "Manifest", manifestId.ToString()), cancellationToken);
        return new ManifestShipmentResponse(manifestShipment.Id, scanInfo.ShipmentId, scanInfo.Awb, manifestShipment.ScannedAt, manifestShipment.ReceivedAt);
    }

    public async Task<ManifestResponse> CloseAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.close", manifest.ToBranchId, cancellationToken);

        // InTransit is allowed too, not just Received: a manifest where staff never scanned a
        // single shipment during receiving (everything on it turned out missing) must still be
        // closeable, not stuck open forever.
        if (manifest.Status is not (ManifestStatus.InTransit or ManifestStatus.Received))
        {
            throw new ManifestValidationException($"Manifest must be InTransit or Received to close, was {manifest.Status}.");
        }

        manifest.Status = ManifestStatus.Closed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.closed", "Manifest", manifestId.ToString()), cancellationToken);
        return ToResponse(manifest);
    }
}
