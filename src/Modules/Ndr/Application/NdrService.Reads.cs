using System.Security.Claims;
using Courier.Ndr.Domain;
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
    public async Task<List<NdrResponse>> ListAsync(NdrStatus? status, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }

        var ndrs = await query.ToListAsync(cancellationToken);

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "ndr.read", cancellationToken);
        if (accessibleBranchIds is null)
        {
            return ndrs.Select(ToResponse).ToList();
        }

        var responses = new List<NdrResponse>();
        foreach (var ndr in ndrs)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
            if (scanInfo is not null && accessibleBranchIds.Contains(scanInfo.DestinationBranchId))
            {
                responses.Add(ToResponse(ndr));
            }
        }
        return responses;
    }
}
