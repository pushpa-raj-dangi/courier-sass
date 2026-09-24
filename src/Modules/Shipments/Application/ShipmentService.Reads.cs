using System.Security.Claims;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    public async Task<ShipmentResponse?> GetByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && shipment.MerchantId != callerMerchantId)
        {
            return null;
        }

        return await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    public async Task<PagedResult<ShipmentResponse>> ListAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Shipment>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(s => s.MerchantId == callerMerchantId);
        }
        else
        {
            // Staff caller: scope to branches the caller can access for shipment.read. null means
            // "no filter applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's
            // documented contract — a non-null set restricts to shipments touching any of those
            // branches as origin, destination, or current physical location. CurrentBranchId is
            // nullable (in-transit/with-rider shipments), so a restricted-branch caller can still
            // see such a shipment via its origin/destination even while CurrentBranchId is null.
            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                // Materialize to an array before use in the query — mirrors BranchService.cs's
                // established pattern (ListBranchesAsync) for translating a caller's accessible-id
                // set into a SQL predicate; an IReadOnlySet<Guid> is not the same translatable shape
                // an array/List<T> is guaranteed to be.
                var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
                query = query.Where(s =>
                    accessibleBranchIdsArray.Contains(s.OriginBranchId) ||
                    accessibleBranchIdsArray.Contains(s.DestinationBranchId) ||
                    (s.CurrentBranchId != null && accessibleBranchIdsArray.Contains(s.CurrentBranchId.Value)));
            }
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var shipments = await query
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var responses = new List<ShipmentResponse>();
        foreach (var shipment in shipments)
        {
            var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
            responses.Add(ToResponse(shipment, items));
        }
        return new PagedResult<ShipmentResponse>(responses, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        return shipment is null ? null : ToScanInfo(shipment);
    }

    public async Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        return shipment is null ? null : await LoadResponseAsync(shipmentId, cancellationToken);
    }

    public async Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        return shipment is null ? null : ToScanInfo(shipment);
    }

    private static ShipmentScanInfo ToScanInfo(Shipment shipment) => new(
        shipment.Id, shipment.OrganizationId, shipment.Awb, shipment.CurrentStatus,
        shipment.CurrentBranchId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentCustodianRiderId, shipment.CodAmount, shipment.Direction);
}
