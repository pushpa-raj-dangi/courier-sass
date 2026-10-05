using System.Security.Claims;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    public async Task<ShipmentResponse?> GetByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await FindReadableByAwbAsync(awb, actingUser, cancellationToken);
        return shipment is null ? null : await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShipmentEventResponse>?> GetEventsByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await FindReadableByAwbAsync(awb, actingUser, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        // ShipmentEvent carries no OrganizationId; tenant isolation comes from resolving the
        // (tenant-filtered) Shipment first and reading only its events.
        return await _dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipment.Id)
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => new ShipmentEventResponse(e.Id, e.EventType, e.PreviousStatus, e.NewStatus, e.BranchId, e.UserId, e.RiderId, e.Timestamp, e.Notes))
            .ToListAsync(cancellationToken);
    }

    // Single-shipment read rule (CLARIFICATIONS #25): tenant filter (global), merchant callers pinned
    // to their own shipments, staff callers limited to shipments whose origin, destination or
    // current branch is in their shipment.read scope — the same predicate ListAsync uses. A miss
    // is indistinguishable from "does not exist" so AWB existence is never disclosed.
    private async Task<Shipment?> FindReadableByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            return shipment.MerchantId == callerMerchantId ? shipment : null;
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.read", cancellationToken);
        if (accessibleBranchIds is null)
        {
            return shipment;
        }
        var inScope = accessibleBranchIds.Contains(shipment.OriginBranchId) ||
                      accessibleBranchIds.Contains(shipment.DestinationBranchId) ||
                      (shipment.CurrentBranchId is Guid current && accessibleBranchIds.Contains(current));
        return inScope ? shipment : null;
    }

    public async Task<PagedResult<ShipmentResponse>> ListAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, ShipmentListFilter? filter = null, CancellationToken cancellationToken = default)
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

        if (filter is not null)
        {
            query = ApplyFilter(query, filter);
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var shipments = await query
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        // One query each for the page's items and latest event times (replaces a per-row items
        // query). Output is identical to the former per-row loading.
        var shipmentIds = shipments.Select(s => s.Id).ToArray();
        var itemsByShipment = (await _dbContext.Set<ShipmentItem>()
                .Where(i => shipmentIds.Contains(i.ShipmentId))
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.ShipmentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var lastEventByShipment = await _dbContext.Set<ShipmentEvent>()
            .Where(e => shipmentIds.Contains(e.ShipmentId))
            .GroupBy(e => e.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, Last = g.Max(e => e.Timestamp) })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.Last, cancellationToken);

        var responses = shipments
            .Select(s => ToResponse(
                s,
                itemsByShipment.TryGetValue(s.Id, out var items) ? items : new List<ShipmentItem>(),
                lastEventByShipment.TryGetValue(s.Id, out var last) ? last : null))
            .ToList();
        return new PagedResult<ShipmentResponse>(responses, normalizedPage, normalizedPageSize, totalCount);
    }

    private static IQueryable<Shipment> ApplyFilter(IQueryable<Shipment> query, ShipmentListFilter filter)
    {
        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToArray();
            query = query.Where(s => statuses.Contains(s.CurrentStatus));
        }
        if (filter.Query is string raw)
        {
            // StartsWith/Contains translate to LIKE with EF's own escaping of % and _, so user
            // input is matched literally on Npgsql; InMemory evaluates them as plain string ops.
            var upper = raw.ToUpperInvariant();
            var lower = raw.ToLowerInvariant();
            query = query.Where(s =>
                s.Awb.ToUpper().StartsWith(upper) ||
                s.RecipientName.ToLower().Contains(lower) ||
                s.RecipientPhone.Contains(raw));
        }
        if (filter.MerchantId is Guid merchantId)
        {
            query = query.Where(s => s.MerchantId == merchantId);
        }
        if (filter.ServiceTypeId is Guid serviceTypeId)
        {
            query = query.Where(s => s.ServiceTypeId == serviceTypeId);
        }
        if (filter.BranchId is Guid branchId)
        {
            query = query.Where(s => s.OriginBranchId == branchId || s.DestinationBranchId == branchId || s.CurrentBranchId == branchId);
        }
        if (filter.CreatedFrom is DateTimeOffset from)
        {
            query = query.Where(s => s.CreatedAt >= from);
        }
        if (filter.CreatedTo is DateTimeOffset to)
        {
            query = query.Where(s => s.CreatedAt <= to);
        }
        return query;
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
