using System.Security.Claims;
using Courier.Ndr.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> GetByIdAsync(Guid ndrId, CancellationToken cancellationToken = default)
    {
        var ndr = await _dbContext.Set<NDR>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        return ndr is null ? null : ToResponse(ndr);
    }

    // Branch-scoped the same way RiderService.ListAsync (Phase 8) and ShipmentService.ListAsync
    // (Phase 6) already are: null from GetAccessibleBranchIdsAsync means AllBranches (no filter).
    // NDR has no branch column of its own — its shipment's DestinationBranchId (the branch that
    // attempted delivery and owns following up, same branch UpdateAsync's CheckBranchAccessAsync
    // call already scopes to) is the relevant scope. That requires a per-row shipment lookup via
    // IShipmentTransitionService.GetScanInfoAsync, which can't be pushed into the SQL query, so we
    // only pay that cost when the caller is actually branch-restricted.
    public async Task<PagedResult<NdrResponse>> ListAsync(NdrStatus? status, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);

        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }
        query = query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id);

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "ndr.read", cancellationToken);
        if (accessibleBranchIds is null)
        {
            // AllBranches scope: no per-row branch check needed, so pagination happens at the SQL
            // level like every other paginated list in this change.
            var totalCount = await query.CountAsync(cancellationToken);
            var ndrs = await query
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            return new PagedResult<NdrResponse>(ndrs.Select(ToResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
        }

        // Branch-restricted caller: NDR has no branch column of its own (NdrConfiguration.cs), so
        // the branch check requires a per-row shipment lookup that can't be pushed into SQL — same
        // shape ListAsync already used before pagination. Pagination is applied after the
        // in-memory filter for this path only (documented limitation, not a regression).
        var candidates = await query.ToListAsync(cancellationToken);
        var scoped = new List<NDR>();
        foreach (var ndr in candidates)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
            if (scanInfo is not null && accessibleBranchIds.Contains(scanInfo.DestinationBranchId))
            {
                scoped.Add(ndr);
            }
        }

        var pagedScoped = scoped.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();
        return new PagedResult<NdrResponse>(pagedScoped.Select(ToResponse).ToList(), normalizedPage, normalizedPageSize, scoped.Count);
    }
}
