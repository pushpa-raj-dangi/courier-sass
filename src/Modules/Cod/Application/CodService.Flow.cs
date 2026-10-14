using System.Security.Claims;
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

// F15 R3a read slice. "Reached" (cumulative) semantics: a collection counts in every stage it has
// reached so far. Parcel counts never increase left to right; amounts never increase from collected
// onward; expected (ExpectedAmount) can be below collected when riders collect more than expected
// (overages). Each gap is money waiting at that step. Distinct from GetReconciliationSummaryAsync's outstanding-only ExpectedCod
// (CLARIFICATIONS: COD flow stages).
public partial class CodService
{
    private static readonly CodCollectionStatus[] CollectedOrLater =
        { CodCollectionStatus.CodCollected, CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
    private static readonly CodCollectionStatus[] RiderDepositedOrLater =
        { CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
    private static readonly CodCollectionStatus[] BranchReconciledOrLater =
        { CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
    private static readonly CodCollectionStatus[] FinanceVerifiedOrLater =
        { CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };

    private sealed record FlowGroup(CodCollectionStatus Status, int Count, decimal Expected, decimal CollectedSum);

    public async Task<CodFlowResponse> GetFlowAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        var query = await StaffScopedCollectionsAsync(organizationId, branchId, actingUser, cancellationToken);
        if (fromUtc is DateTimeOffset fromDate)
        {
            query = query.Where(c => c.CreatedAt >= fromDate);
        }
        if (toUtc is DateTimeOffset toDate)
        {
            query = query.Where(c => c.CreatedAt <= toDate);
        }

        // One grouped SQL query (at most 7 statuses × 2 rows) — never materialises collections.
        var groups = await query
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Expected = g.Sum(c => c.ExpectedAmount), CollectedSum = g.Sum(c => c.CollectedAmount ?? 0m) })
            .ToListAsync(cancellationToken);
        var rows = groups.Select(g => new FlowGroup(g.Status, g.Count, g.Expected, g.CollectedSum)).ToList();

        return new CodFlowResponse(new List<CodFlowStageResponse>
        {
            new("expected", rows.Sum(r => r.Expected), rows.Sum(r => r.Count)),
            Reached("collected", rows.Where(r => CollectedOrLater.Contains(r.Status))),
            Reached("riderDeposited", rows.Where(r => RiderDepositedOrLater.Contains(r.Status))),
            Reached("branchReconciled", rows.Where(r => BranchReconciledOrLater.Contains(r.Status))),
            Reached("financeVerified", rows.Where(r => FinanceVerifiedOrLater.Contains(r.Status))),
            Reached("settled", rows.Where(r => r.Status == CodCollectionStatus.Settled)),
        });
    }

    public async Task<RiderCashResponse> GetRiderCashAsync(Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = await StaffScopedCollectionsAsync(organizationId, branchId, actingUser, cancellationToken);
        var rows = await query
            .Where(c => c.Status == CodCollectionStatus.CodCollected && c.CollectedByRiderId != null && c.CollectedAt != null)
            .GroupBy(c => new { RiderId = c.CollectedByRiderId!.Value, c.BranchId })
            .Select(g => new
            {
                g.Key.RiderId,
                g.Key.BranchId,
                Count = g.Count(),
                Amount = g.Sum(c => c.CollectedAmount ?? 0m),
                Oldest = g.Min(c => c.CollectedAt!.Value),
                Latest = g.Max(c => c.CollectedAt!.Value),
            })
            .ToListAsync(cancellationToken);

        return new RiderCashResponse(rows
            .OrderBy(r => r.Oldest)
            .Select(r => new RiderCashHoldingResponse(r.RiderId, r.BranchId, r.Count, r.Amount, r.Oldest, r.Latest))
            .ToList());
    }

    private static CodFlowStageResponse Reached(string key, IEnumerable<FlowGroup> rows)
    {
        var list = rows.ToList();
        return new CodFlowStageResponse(key, list.Sum(r => r.CollectedSum), list.Sum(r => r.Count));
    }

    // Staff-only scope (endpoints reject merchant callers before reaching here): tenant filter,
    // optional branch narrowing, then cod.read branch access.
    private async Task<IQueryable<CODCollection>> StaffScopedCollectionsAsync(Guid organizationId, Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var query = _dbContext.Set<CODCollection>().Where(c => c.OrganizationId == organizationId);
        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(c => c.BranchId == requestedBranchId);
        }
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleArray = accessibleBranchIds.ToArray();
            query = query.Where(c => accessibleArray.Contains(c.BranchId));
        }
        return query;
    }
}
