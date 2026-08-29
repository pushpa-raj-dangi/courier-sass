using System.Security.Claims;
using Courier.Manifests.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestResponse?> GetByIdAsync(Guid manifestId, CancellationToken cancellationToken = default)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        return manifest is null ? null : ToResponse(manifest);
    }

    public async Task<List<ManifestResponse>> ListAsync(Guid? branchId, ManifestStatus? status, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Manifest>().AsQueryable();

        // Scope to branches the caller can access for manifest.read. null means "no filter
        // applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's documented
        // contract — a non-null set restricts to manifests touching any of those branches as
        // sender or receiver, mirroring ShipmentService.Reads.cs's ListAsync's established shape.
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "manifest.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
            query = query.Where(m =>
                accessibleBranchIdsArray.Contains(m.FromBranchId) ||
                accessibleBranchIdsArray.Contains(m.ToBranchId));
        }

        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(m => m.FromBranchId == requestedBranchId || m.ToBranchId == requestedBranchId);
        }
        if (status is ManifestStatus requestedStatus)
        {
            query = query.Where(m => m.Status == requestedStatus);
        }

        var manifests = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(cancellationToken);
        return manifests.Select(ToResponse).ToList();
    }

    // Meaningful once the manifest is Closed (a null ReceivedAt then IS the missing-shipment
    // exception per this plan's resolved design decision #4) — also usable while a manifest is
    // still InTransit/Received to show which shipments haven't been scanned in yet.
    public async Task<List<MissingShipmentResponse>?> GetMissingShipmentsAsync(Guid manifestId, CancellationToken cancellationToken = default)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var unreceived = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId && ms.ReceivedAt == null)
            .ToListAsync(cancellationToken);

        var result = new List<MissingShipmentResponse>();
        foreach (var manifestShipment in unreceived)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(manifestShipment.ShipmentId, cancellationToken);
            result.Add(new MissingShipmentResponse(manifestShipment.ShipmentId, scanInfo?.Awb ?? string.Empty, manifestShipment.ScannedAt));
        }
        return result;
    }
}
