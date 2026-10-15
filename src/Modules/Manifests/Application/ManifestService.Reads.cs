using System.Security.Claims;
using Courier.Manifests.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestResponse?> GetByIdAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var manifest = await FindReadableAsync(manifestId, actingUser, cancellationToken);
        return manifest is null ? null : await ToResponseAsync(manifest, cancellationToken);
    }

    // direction (F15 R3b-1): "incoming" keeps manifests headed TO the branch (the requested
    // branchId, else any branch in the caller's scope); "outgoing" the same on FromBranchId;
    // omitted keeps today's "touches the branch either way". It only narrows the scope filter.
    public async Task<List<ManifestResponse>> ListAsync(Guid? branchId, ManifestStatus? status, string? direction, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var normalizedDirection = string.IsNullOrWhiteSpace(direction) ? null : direction.Trim().ToLowerInvariant();
        if (normalizedDirection is not (null or "incoming" or "outgoing"))
        {
            throw new ManifestValidationException("direction must be 'incoming' or 'outgoing'.");
        }

        var query = _dbContext.Set<Manifest>().AsQueryable();

        // Scope to branches the caller can access for manifest.read. null means "no filter
        // applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's documented
        // contract — a non-null set restricts to manifests touching any of those branches as
        // sender or receiver, mirroring ShipmentService.Reads.cs's ListAsync's established shape.
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "manifest.read", cancellationToken);
        var accessibleBranchIdsArray = accessibleBranchIds?.ToArray();
        if (accessibleBranchIdsArray is not null)
        {
            query = query.Where(m =>
                accessibleBranchIdsArray.Contains(m.FromBranchId) ||
                accessibleBranchIdsArray.Contains(m.ToBranchId));
        }

        if (branchId is Guid requestedBranchId)
        {
            query = normalizedDirection switch
            {
                "incoming" => query.Where(m => m.ToBranchId == requestedBranchId),
                "outgoing" => query.Where(m => m.FromBranchId == requestedBranchId),
                _ => query.Where(m => m.FromBranchId == requestedBranchId || m.ToBranchId == requestedBranchId)
            };
        }
        else if (accessibleBranchIdsArray is not null)
        {
            if (normalizedDirection == "incoming")
            {
                query = query.Where(m => accessibleBranchIdsArray.Contains(m.ToBranchId));
            }
            else if (normalizedDirection == "outgoing")
            {
                query = query.Where(m => accessibleBranchIdsArray.Contains(m.FromBranchId));
            }
        }

        if (status is ManifestStatus requestedStatus)
        {
            query = query.Where(m => m.Status == requestedStatus);
        }

        var manifests = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(cancellationToken);

        // One grouped query for every listed manifest's received count — no per-row CountAsync.
        var manifestIds = manifests.Select(m => m.Id).ToArray();
        var receivedCounts = await _dbContext.Set<ManifestShipment>()
            .Where(ms => manifestIds.Contains(ms.ManifestId) && ms.ReceivedAt != null)
            .GroupBy(ms => ms.ManifestId)
            .Select(g => new { ManifestId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ManifestId, x => x.Count, cancellationToken);

        return manifests.Select(m => ToResponse(m, receivedCounts.GetValueOrDefault(m.Id))).ToList();
    }

    // Meaningful once the manifest is Closed (a null ReceivedAt then IS the missing-shipment
    // exception per this plan's resolved design decision #4) — also usable while a manifest is
    // still InTransit/Received to show which shipments haven't been scanned in yet. AWBs come from
    // one batched lookup (F15 R3b-1), not a per-row scan-info call.
    public async Task<List<MissingShipmentResponse>?> GetMissingShipmentsAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var manifest = await FindReadableAsync(manifestId, actingUser, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var unreceived = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId && ms.ReceivedAt == null)
            .OrderBy(ms => ms.ScannedAt).ThenBy(ms => ms.Id)
            .ToListAsync(cancellationToken);
        var awbs = await _shipmentReportingLookupService.GetAwbsByShipmentIdsAsync(
            unreceived.Select(ms => ms.ShipmentId).ToHashSet(), cancellationToken);

        return unreceived
            .Select(ms => new MissingShipmentResponse(ms.ShipmentId, awbs.GetValueOrDefault(ms.ShipmentId, string.Empty), ms.ScannedAt))
            .ToList();
    }

    // Every parcel on the manifest (F15 R3b-1). Not paginated: bounded by one vehicle load.
    public async Task<List<ManifestShipmentResponse>?> ListShipmentsAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var manifest = await FindReadableAsync(manifestId, actingUser, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var parcels = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId)
            .OrderBy(ms => ms.ScannedAt).ThenBy(ms => ms.Id)
            .ToListAsync(cancellationToken);
        var awbs = await _shipmentReportingLookupService.GetAwbsByShipmentIdsAsync(
            parcels.Select(ms => ms.ShipmentId).ToHashSet(), cancellationToken);

        return parcels
            .Select(ms => new ManifestShipmentResponse(ms.Id, ms.ShipmentId, awbs.GetValueOrDefault(ms.ShipmentId, string.Empty), ms.ScannedAt, ms.ReceivedAt))
            .ToList();
    }

    // Single reads use the list's rule: readable when FromBranchId or ToBranchId is in the
    // caller's manifest.read scope (null = all branches). Out of scope reads as not found, so a
    // guessed id never confirms that a manifest exists.
    private async Task<Manifest?> FindReadableAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "manifest.read", cancellationToken);
        if (accessibleBranchIds is not null &&
            !accessibleBranchIds.Contains(manifest.FromBranchId) &&
            !accessibleBranchIds.Contains(manifest.ToBranchId))
        {
            return null;
        }
        return manifest;
    }
}
