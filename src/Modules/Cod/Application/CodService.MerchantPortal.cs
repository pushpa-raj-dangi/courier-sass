using System.Security.Claims;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

// F15 R5b-1: merchant portal reads. Every method is hard-scoped to the caller's own merchant id
// (from the token, never a client value) plus the current organization.
public partial class CodService
{
    // Collected by a rider (or further along the cash chain) but not yet folded into a settlement.
    private static readonly CodCollectionStatus[] CollectedNotSettledStatuses =
    {
        CodCollectionStatus.CodCollected, CodCollectionStatus.RiderDeposited,
        CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified
    };

    private static Guid RequireMerchant(ClaimsPrincipal actingUser) =>
        actingUser.GetMerchantId() ?? throw new CodValidationException("Merchant context required.");

    private IQueryable<CODCollection> AwaitingQuery(Guid organizationId, Guid merchantId) =>
        _dbContext.Set<CODCollection>().Where(c =>
            c.OrganizationId == organizationId && c.MerchantId == merchantId
            && c.MerchantSettlementItemId == null && CollectedNotSettledStatuses.Contains(c.Status));

    public async Task<MerchantCodSummaryResponse> GetMerchantCodSummaryAsync(ClaimsPrincipal actingUser, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var merchantId = RequireMerchant(actingUser);
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var tz = await _organizationTimeZoneLookup.GetTimeZoneAsync(organizationId, cancellationToken);
        var today = OrgCalendar.Today(tz, nowUtc);
        var month = OrgCalendar.ThisMonth(tz, nowUtc);

        var awaitingRows = await AwaitingQuery(organizationId, merchantId)
            .Select(c => c.CollectedAmount ?? 0m).ToListAsync(cancellationToken);

        var settlements = _dbContext.Set<MerchantSettlement>().Where(m => m.OrganizationId == organizationId && m.MerchantId == merchantId);
        var inSettlementRows = await settlements.Where(m => m.Status == MerchantSettlementStatus.Created)
            .Select(m => m.TotalPayable).ToListAsync(cancellationToken);

        var settledRows = await settlements
            .Where(m => m.Status == MerchantSettlementStatus.Settled && m.SettledAt >= month.FromUtc && m.SettledAt < month.ToUtc)
            .Select(m => new { m.TotalPayable, m.SettledAt })
            .ToListAsync(cancellationToken);

        var collectedTodayRows = await _dbContext.Set<CODCollection>()
            .Where(c => c.OrganizationId == organizationId && c.MerchantId == merchantId
                && c.CollectedAt >= today.FromUtc && c.CollectedAt < today.ToUtc)
            .Select(c => c.CollectedAmount ?? 0m).ToListAsync(cancellationToken);
        var delivered = await _shipmentDeliveryLookupService.CountDeliveredAsync(merchantId, today.FromUtc, today.ToUtc, cancellationToken);

        return new MerchantCodSummaryResponse(
            new MoneyBucket(awaitingRows.Sum(), awaitingRows.Count),
            new MoneyBucket(inSettlementRows.Sum(), inSettlementRows.Count),
            new SettledBucket(settledRows.Sum(r => r.TotalPayable), settledRows.Count, settledRows.Max(r => r.SettledAt)),
            new DeliveredTodayBucket(delivered, collectedTodayRows.Sum()));
    }

    public async Task<PagedResult<MerchantAwaitingCollectionResponse>> ListMerchantAwaitingAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = RequireMerchant(actingUser);
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = AwaitingQuery(organizationId, merchantId);
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(c => c.CollectedAt).ThenBy(c => c.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var awbs = await _shipmentAwbLookupService.GetAwbsByShipmentIdsAsync(rows.Select(c => c.ShipmentId).ToHashSet(), cancellationToken);
        var items = rows.Select(c =>
        {
            var collected = c.CollectedAmount ?? 0m;
            return new MerchantAwaitingCollectionResponse(
                c.ShipmentId, awbs.TryGetValue(c.ShipmentId, out var awb) ? awb : string.Empty,
                c.CollectedAt ?? c.CreatedAt, collected, c.ShipmentTotalCharge, collected - c.ShipmentTotalCharge);
        }).ToList();
        return new PagedResult<MerchantAwaitingCollectionResponse>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task<MerchantShipmentCodStatusResponse?> GetMerchantShipmentCodStatusAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = RequireMerchant(actingUser);
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        if (!await _shipmentDeliveryLookupService.BelongsToMerchantAsync(shipmentId, merchantId, cancellationToken))
        {
            return null;
        }

        var collection = await _dbContext.Set<CODCollection>().FirstOrDefaultAsync(
            c => c.ShipmentId == shipmentId && c.MerchantId == merchantId && c.OrganizationId == organizationId, cancellationToken);
        if (collection is null)
        {
            return new MerchantShipmentCodStatusResponse(MerchantCodState.Prepaid, null, null, null, null);
        }
        if (collection.Status == CodCollectionStatus.CodExpected)
        {
            return new MerchantShipmentCodStatusResponse(MerchantCodState.NotCollected, null, null, null, null);
        }
        if (collection.MerchantSettlementItemId is not Guid itemId)
        {
            return new MerchantShipmentCodStatusResponse(MerchantCodState.CollectedAwaitingSettlement, null, null, null, null);
        }

        var settlement = await (
            from item in _dbContext.Set<MerchantSettlementItem>()
            join s in _dbContext.Set<MerchantSettlement>() on item.MerchantSettlementId equals s.Id
            where item.Id == itemId && s.OrganizationId == organizationId && s.MerchantId == merchantId
            select s).FirstOrDefaultAsync(cancellationToken);
        if (settlement is null)
        {
            return new MerchantShipmentCodStatusResponse(MerchantCodState.CollectedAwaitingSettlement, null, null, null, null);
        }
        return settlement.Status == MerchantSettlementStatus.Settled
            ? new MerchantShipmentCodStatusResponse(MerchantCodState.Settled, settlement.Id, settlement.PeriodFrom, settlement.PeriodTo, settlement.SettledAt)
            : new MerchantShipmentCodStatusResponse(MerchantCodState.InSettlement, settlement.Id, settlement.PeriodFrom, settlement.PeriodTo, null);
    }
}
