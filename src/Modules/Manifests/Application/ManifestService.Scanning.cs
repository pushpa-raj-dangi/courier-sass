using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Manifests.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestShipmentResponse> ScanAsync(Guid manifestId, ScanShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");
        if (string.IsNullOrWhiteSpace(request.Awb))
        {
            throw new ManifestValidationException("Awb is required.");
        }

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        // Check 1 (permission) is enforced at the route via .RequirePermission("manifest.create");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "manifest.create", manifest.FromBranchId, cancellationToken);

        if (manifest.Status != ManifestStatus.Draft)
        {
            throw new ManifestScanRejectedException("MANIFEST_NOT_OPEN", $"Manifest is {manifest.Status}, not open for adding shipments.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new ManifestScanRejectedException("SHIPMENT_NOT_FOUND", $"No shipment found for AWB '{request.Awb}'.");

        // Check 3 (shipment state) + check 4 (expected operation) collapse into one check here: the
        // only operation this endpoint performs IS the AtOriginBranch -> Sorted transition, so
        // "shipment is AtOriginBranch" fully captures both.
        if (scanInfo.CurrentStatus != ShipmentStatus.AtOriginBranch)
        {
            throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be AtOriginBranch to be added to a manifest, was {scanInfo.CurrentStatus}.");
        }
        // Check 5 (custody consistency): the shipment must physically be at the branch this
        // manifest departs from.
        if (scanInfo.CurrentBranchId != manifest.FromBranchId)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's current branch does not match this manifest's FromBranchId.");
        }

        await _shipmentTransitionService.SortAsync(scanInfo.ShipmentId, actingUser, cancellationToken);

        var manifestShipment = new ManifestShipment
        {
            Id = Guid.NewGuid(),
            ManifestId = manifestId,
            ShipmentId = scanInfo.ShipmentId,
            ScannedAt = DateTimeOffset.UtcNow,
            ReceivedAt = null
        };
        _dbContext.Set<ManifestShipment>().Add(manifestShipment);
        manifest.ShipmentCount += 1;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.shipment_scanned", "Manifest", manifestId.ToString()), cancellationToken);
        return new ManifestShipmentResponse(manifestShipment.Id, scanInfo.ShipmentId, scanInfo.Awb, manifestShipment.ScannedAt, manifestShipment.ReceivedAt);
    }

    public async Task<ManifestResponse> SealAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.seal", manifest.FromBranchId, cancellationToken);

        if (manifest.Status != ManifestStatus.Draft)
        {
            throw new ManifestValidationException($"Manifest must be Draft to seal, was {manifest.Status}.");
        }
        if (manifest.ShipmentCount == 0)
        {
            throw new ManifestValidationException("Manifest has no shipments to seal.");
        }

        manifest.Status = ManifestStatus.Sealed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.sealed", "Manifest", manifestId.ToString()), cancellationToken);
        return ToResponse(manifest);
    }
}
