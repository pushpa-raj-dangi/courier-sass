using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public class CodReportingLookupService : ICodReportingLookupService
{
    private readonly DbContext _dbContext;

    public CodReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<CODCollection>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(c => branchIdArray.Contains(c.BranchId));
        }

        var expected = await query.Where(c => c.Status != CodCollectionStatus.Settled).SumAsync(c => c.ExpectedAmount, cancellationToken);
        var collected = await query.Where(c => c.Status != CodCollectionStatus.CodExpected).SumAsync(c => c.CollectedAmount ?? 0m, cancellationToken);

        // MerchantSettlement has no BranchId of its own (it spans every branch that delivered for
        // a merchant) — a branch-restricted caller still sees the org-wide settled/payable totals
        // here, the same "no branch concept" shape CodService.ListMerchantSettlementsAsync already
        // has for its own reads.
        var settlements = _dbContext.Set<MerchantSettlement>().AsNoTracking().AsQueryable();
        var settled = await settlements.Where(s => s.Status == MerchantSettlementStatus.Settled).SumAsync(s => s.TotalPayable, cancellationToken);
        var merchantPayable = await settlements.Where(s => s.Status == MerchantSettlementStatus.Created).SumAsync(s => s.TotalPayable, cancellationToken);

        // Pending must be derived from CODCollection (the already branch-filtered `query`), not from
        // MerchantSettlement.TotalPayable: TotalPayable is net-of-courier-fee (CLAUDE.md §22: COD 2500 -
        // fee 150 = payable 2350), while `collected` is the gross CollectedAmount sum, so subtracting a net
        // figure from a gross figure overstated Pending by the accumulated courier fees. MerchantSettlement
        // is also never branch-filtered (it has no BranchId), while `collected` is branch-filtered when the
        // caller has a branch scope, so a branch-scoped caller could see a negative Pending.
        var settledCod = await query.Where(c => c.Status == CodCollectionStatus.Settled).SumAsync(c => c.CollectedAmount ?? 0m, cancellationToken);

        return new CodFinancialSummary(expected, collected, collected - settledCod, settled, merchantPayable);
    }

    public async Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var statuses = new[] { CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled };
        return await _dbContext.Set<CODCollection>()
            .AsNoTracking()
            .Where(c => c.BranchId == branchId && statuses.Contains(c.Status))
            .SumAsync(c => c.CollectedAmount ?? 0m, cancellationToken);
    }

    // Mirrors CodService.Reads.cs's GetReconciliationSummaryAsync date-filter convention exactly:
    // a null CollectedAt (not yet collected) is never excluded by the range, since an uncollected
    // COD hasn't happened yet and so can't be said to fall outside any date range.
    public async Task<IReadOnlyList<CodExportRow>> GetCodExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<CODCollection>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(c => branchIdArray.Contains(c.BranchId));
        }
        query = query
            .Where(c => c.CollectedAt == null || c.CollectedAt >= from)
            .Where(c => c.CollectedAt == null || c.CollectedAt <= to);

        var rows = await query
            .Select(c => new CodExportRow(c.ShipmentId, c.MerchantId, c.BranchId, c.ExpectedAmount, c.CollectedAmount, c.Status, c.CollectedAt))
            .ToListAsync(cancellationToken);
        return rows;
    }
}
