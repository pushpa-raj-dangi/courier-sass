using System.Security.Claims;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    public async Task<CodCollectionResponse?> GetCollectionByIdAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var collection = await _dbContext.Set<CODCollection>()
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == organizationId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        // Same scoping rules as ListCollectionsAsync below — a merchant-JWT caller is hard-scoped to
        // their own id, a staff caller is scoped to cod.read-accessible branches.
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            if (collection.MerchantId != callerMerchantId)
            {
                return null;
            }
        }
        else
        {
            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
            if (accessibleBranchIds is not null && !accessibleBranchIds.Contains(collection.BranchId))
            {
                return null;
            }
        }

        return ToResponse(collection);
    }

    // BranchId lives directly on CODCollection (this plan's eager-creation snapshot design) — a
    // single SQL WHERE clause, unlike Ndr.Application.NdrService.ListAsync's per-row
    // IShipmentTransitionService.GetScanInfoAsync lookup loop (Phase 9), which needed one because
    // NDR has no branch column of its own.
    public async Task<PagedResult<CodCollectionResponse>> ListCollectionsAsync(CodCollectionStatus? status, Guid? branchId, Guid? merchantId, Guid? riderCashSettlementId, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<CODCollection>().Where(c => c.OrganizationId == organizationId);
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }
        if (riderCashSettlementId is Guid requestedSettlementId)
        {
            query = query.Where(c => c.RiderCashSettlementId == requestedSettlementId);
        }

        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            // Merchant-JWT callers are hard-scoped to their own id regardless of what the query
            // string says — never trust a client-supplied MerchantId on a merchant token
            // (CLAUDE.md §33), same precedent as Shipments.Application.ShipmentService.CreateShipmentAsync.
            query = query.Where(c => c.MerchantId == callerMerchantId);
        }
        else
        {
            if (merchantId is Guid requestedMerchantId)
            {
                query = query.Where(c => c.MerchantId == requestedMerchantId);
            }
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
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var collections = await query
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<CodCollectionResponse>(collections.Select(ToResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
    }

    // CLAUDE.md §23's reconciliation figures, computed over whatever scope ListCollectionsAsync
    // above would also apply (merchant-JWT self-scope, or staff branch/merchant filters +
    // cod.read branch access) plus an optional CollectedAt date-range filter. A CODCollection still
    // at CodExpected has a null CollectedAt and is never excluded by the date filter — it hasn't
    // happened yet, so it can't be said to fall outside any date range.
    public async Task<CodReconciliationSummaryResponse> GetReconciliationSummaryAsync(Guid? branchId, Guid? merchantId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<CODCollection>().Where(c => c.OrganizationId == organizationId);
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(c => c.MerchantId == callerMerchantId);
        }
        else
        {
            if (merchantId is Guid requestedMerchantId)
            {
                query = query.Where(c => c.MerchantId == requestedMerchantId);
            }
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
        }
        if (from is DateTimeOffset fromDate)
        {
            query = query.Where(c => c.CollectedAt == null || c.CollectedAt >= fromDate);
        }
        if (to is DateTimeOffset toDate)
        {
            query = query.Where(c => c.CollectedAt == null || c.CollectedAt <= toDate);
        }

        // Pushed to SQL as five separate SumAsync calls against the same filtered `query` (rather
        // than one ToListAsync() + five LINQ-to-objects sums) so an org's dashboard load never
        // materializes the full CODCollection result set into memory.
        var reachedStatuses = new[] { CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
        var branchReceivedStatuses = new[] { CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };

        var expectedCod = await query.Where(c => c.Status == CodCollectionStatus.CodExpected).SumAsync(c => c.ExpectedAmount, cancellationToken);
        var collectedCod = await query.Where(c => c.CollectedAmount != null).SumAsync(c => c.CollectedAmount!.Value, cancellationToken);
        var riderDepositedCod = await query.Where(c => reachedStatuses.Contains(c.Status)).SumAsync(c => c.CollectedAmount!.Value, cancellationToken);
        var branchReceivedCod = await query.Where(c => branchReceivedStatuses.Contains(c.Status)).SumAsync(c => c.CollectedAmount!.Value, cancellationToken);
        var totalCourierFees = await query.Where(c => c.CollectedAmount != null).SumAsync(c => c.ShipmentTotalCharge, cancellationToken);

        // OrganizationId filter is required here too — MerchantSettlementItem has no OrganizationId
        // column of its own, so this join relies on the settlementItemIds already having been
        // sourced from an org-scoped CODCollection query above rather than re-checking org here.
        var settlementItemIds = await query.Where(c => c.MerchantSettlementItemId != null).Select(c => c.MerchantSettlementItemId!.Value).ToArrayAsync(cancellationToken);
        var merchantPayableCod = settlementItemIds.Length == 0
            ? 0m
            : await _dbContext.Set<MerchantSettlementItem>().Where(i => settlementItemIds.Contains(i.Id)).SumAsync(i => i.NetPayable, cancellationToken);

        return new CodReconciliationSummaryResponse(expectedCod, collectedCod, riderDepositedCod, branchReceivedCod, merchantPayableCod, totalCourierFees);
    }

    public async Task<List<MerchantSettlementResponse>> ListMerchantSettlementsAsync(Guid? merchantId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<MerchantSettlement>().Where(m => m.OrganizationId == organizationId);
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(m => m.MerchantId == callerMerchantId);
        }
        else if (merchantId is Guid requestedMerchantId)
        {
            query = query.Where(m => m.MerchantId == requestedMerchantId);
        }

        var settlements = await query.ToListAsync(cancellationToken);
        var settlementIds = settlements.Select(s => s.Id).ToHashSet();
        var allItems = await _dbContext.Set<MerchantSettlementItem>()
            .Where(i => settlementIds.Contains(i.MerchantSettlementId)).ToListAsync(cancellationToken);
        var awbsById = await _shipmentAwbLookupService.GetAwbsByShipmentIdsAsync(
            allItems.Select(i => i.ShipmentId).ToHashSet(), cancellationToken);

        var responses = new List<MerchantSettlementResponse>();
        foreach (var settlement in settlements)
        {
            var items = allItems.Where(i => i.MerchantSettlementId == settlement.Id).ToList();
            responses.Add(ToMerchantSettlementResponse(settlement, items, awbsById));
        }
        return responses;
    }

    public async Task<MerchantSettlementResponse?> GetMerchantSettlementByIdAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var settlement = await _dbContext.Set<MerchantSettlement>()
            .FirstOrDefaultAsync(m => m.Id == id && m.OrganizationId == organizationId, cancellationToken);
        if (settlement is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && settlement.MerchantId != callerMerchantId)
        {
            return null;
        }

        var items = await _dbContext.Set<MerchantSettlementItem>().Where(i => i.MerchantSettlementId == id).ToListAsync(cancellationToken);
        var awbsById = await _shipmentAwbLookupService.GetAwbsByShipmentIdsAsync(
            items.Select(i => i.ShipmentId).ToHashSet(), cancellationToken);
        return ToMerchantSettlementResponse(settlement, items, awbsById);
    }

    // Mirrors ListCollectionsAsync's shape exactly: RiderCashSettlement carries its own BranchId
    // (Task 6 of Phase 10's original plan), so branch-access scoping is a direct WHERE, no join.
    // unreconciledOnly drives the Branch Settlements create-Sheet's rider-settlement picker
    // (Frontend Phase F7).
    public async Task<PagedResult<RiderCashSettlementResponse>> ListRiderSettlementsAsync(
        Guid? branchId, Guid? riderId, bool? unreconciledOnly, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        var query = _dbContext.Set<RiderCashSettlement>().Where(r => r.OrganizationId == organizationId);
        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(r => r.BranchId == requestedBranchId);
        }
        if (riderId is Guid requestedRiderId)
        {
            query = query.Where(r => r.RiderId == requestedRiderId);
        }
        if (unreconciledOnly == true)
        {
            query = query.Where(r => r.BranchCashSettlementId == null);
        }
        if (fromUtc is DateTimeOffset fromDate)
        {
            query = query.Where(r => r.SettledAt >= fromDate);
        }
        if (toUtc is DateTimeOffset toDate)
        {
            query = query.Where(r => r.SettledAt <= toDate);
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleArray = accessibleBranchIds.ToArray();
            query = query.Where(r => accessibleArray.Contains(r.BranchId));
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var settlements = await query
            .OrderByDescending(r => r.SettledAt).ThenBy(r => r.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        // One grouped query for the whole page (no N+1): each settlement's expected amount is the
        // sum of what its linked collections actually collected.
        var pageIds = settlements.Select(r => r.Id).ToArray();
        var expectedById = await _dbContext.Set<CODCollection>()
            .Where(c => c.OrganizationId == organizationId && c.RiderCashSettlementId != null && pageIds.Contains(c.RiderCashSettlementId.Value))
            .GroupBy(c => c.RiderCashSettlementId!.Value)
            .Select(g => new { SettlementId = g.Key, Expected = g.Sum(c => c.CollectedAmount ?? 0m) })
            .ToDictionaryAsync(x => x.SettlementId, x => x.Expected, cancellationToken);

        var items = settlements
            .Select(r => ToRiderSettlementResponse(r, expectedById.TryGetValue(r.Id, out var expected) ? expected : 0m))
            .ToList();
        return new PagedResult<RiderCashSettlementResponse>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    // unverifiedOnly means "no FinancialTransaction row references this BranchCashSettlement.Id
    // yet" — mirrors CreateFinanceVerificationAsync's own alreadyVerified pre-check. Drives the
    // Branch Settlements tab's "unverified only" filter (Frontend Phase F7).
    public async Task<PagedResult<BranchCashSettlementResponse>> ListBranchSettlementsAsync(
        Guid? branchId, bool? unverifiedOnly, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<BranchCashSettlement>().Where(b => b.OrganizationId == organizationId);
        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(b => b.BranchId == requestedBranchId);
        }
        if (unverifiedOnly == true)
        {
            var verifiedIds = _dbContext.Set<FinancialTransaction>()
                .Where(f => f.OrganizationId == organizationId && f.ReferenceEntityType == "BranchCashSettlement")
                .Select(f => f.ReferenceEntityId);
            query = query.Where(b => !verifiedIds.Contains(b.Id));
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleArray = accessibleBranchIds.ToArray();
            query = query.Where(b => accessibleArray.Contains(b.BranchId));
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var settlements = await query
            .OrderByDescending(b => b.ReconciledAt).ThenBy(b => b.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var settlementIds = settlements.Select(b => b.Id).ToArray();
        // GroupBy composed only into ToDictionaryAsync (no aggregate/projection) does not translate
        // to SQL — materialize the (small, page-bounded) covering rows first, then group client-side.
        var coveringRiderSettlements = await _dbContext.Set<RiderCashSettlement>()
            .Where(r => r.BranchCashSettlementId != null && settlementIds.Contains(r.BranchCashSettlementId.Value))
            .ToListAsync(cancellationToken);
        var riderSettlementIdsBySettlement = coveringRiderSettlements
            .GroupBy(r => r.BranchCashSettlementId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());

        var responses = settlements
            .Select(b => ToBranchSettlementResponse(b, riderSettlementIdsBySettlement.TryGetValue(b.Id, out var ids) ? ids : new List<Guid>()))
            .ToList();
        return new PagedResult<BranchCashSettlementResponse>(responses, normalizedPage, normalizedPageSize, totalCount);
    }

    // Adjustment carries its own OrganizationId column (no join needed, same as
    // ApproveAdjustmentAsync's own lookup). No branch scoping — Adjustment targets a
    // MerchantSettlementItem, which has no BranchId either (cod.settle/cod.adjust are org-wide
    // permissions, same precedent as CreateFinanceVerificationAsync/CreateMerchantSettlementAsync).
    public async Task<PagedResult<AdjustmentResponse>> ListAdjustmentsAsync(
        bool? applied, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<Adjustment>().Where(a => a.OrganizationId == organizationId);
        if (applied is bool requestedApplied)
        {
            query = query.Where(a => a.Applied == requestedApplied);
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var adjustments = await query
            .OrderByDescending(a => a.RequestedAt).ThenBy(a => a.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AdjustmentResponse>(adjustments.Select(ToAdjustmentResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
    }
}
