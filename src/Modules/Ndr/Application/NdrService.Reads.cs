using System.Security.Claims;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> GetByIdAsync(Guid ndrId, CancellationToken cancellationToken = default)
    {
        var ndr = await _dbContext.Set<NDR>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        if (ndr is null)
        {
            return null;
        }
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
        return ToResponse(ndr, scanInfo?.Awb ?? string.Empty);
    }

    // Branch-scoped the same way RiderService.ListAsync (Phase 8) and ShipmentService.ListAsync
    // (Phase 6) already are: null from GetAccessibleBranchIdsAsync means AllBranches (no filter).
    // NDR has no branch column of its own — its shipment's DestinationBranchId (the branch that
    // attempted delivery and owns following up, same branch UpdateAsync's CheckBranchAccessAsync
    // call already scopes to) is the relevant scope. That requires a per-row shipment lookup via
    // IShipmentTransitionService.GetScanInfoAsync, which can't be pushed into the SQL query, so we
    // only pay that cost when the caller is actually branch-restricted.
    public async Task<PagedResult<NdrResponse>> ListAsync(NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);

        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }
        if (shipmentId is not null)
        {
            query = query.Where(n => n.ShipmentId == shipmentId);
        }
        query = query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id);

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "ndr.read", cancellationToken);
        if (accessibleBranchIds is null)
        {
            // AllBranches scope: pagination still happens at the SQL level; each row on the page
            // (bounded by pageSize) is then enriched with its shipment's Awb — the same per-row
            // cost the branch-restricted path below already pays for every candidate.
            var totalCount = await query.CountAsync(cancellationToken);
            var ndrs = await query
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            var pageResponses = new List<NdrResponse>();
            foreach (var ndr in ndrs)
            {
                var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
                pageResponses.Add(ToResponse(ndr, scanInfo?.Awb ?? string.Empty));
            }
            return new PagedResult<NdrResponse>(pageResponses, normalizedPage, normalizedPageSize, totalCount);
        }

        // Branch-restricted caller: NDR has no branch column of its own (NdrConfiguration.cs), so
        // the branch check requires a per-row shipment lookup that can't be pushed into SQL — same
        // shape ListAsync already used before pagination. Pagination is applied after the
        // in-memory filter for this path only (documented limitation, not a regression). The same
        // per-row ShipmentScanInfo fetched for the branch check is cached here and reused for Awb
        // enrichment below, rather than fetched twice for a row that survives filtering.
        var candidates = await query.ToListAsync(cancellationToken);
        var scoped = new List<NDR>();
        var scanInfoByShipmentId = new Dictionary<Guid, ShipmentScanInfo>();
        foreach (var ndr in candidates)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
            if (scanInfo is not null && accessibleBranchIds.Contains(scanInfo.DestinationBranchId))
            {
                scoped.Add(ndr);
                scanInfoByShipmentId[ndr.ShipmentId] = scanInfo;
            }
        }

        var pagedScoped = scoped.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();
        var scopedResponses = pagedScoped.Select(ndr => ToResponse(ndr, scanInfoByShipmentId[ndr.ShipmentId].Awb)).ToList();
        return new PagedResult<NdrResponse>(scopedResponses, normalizedPage, normalizedPageSize, scoped.Count);
    }
}
