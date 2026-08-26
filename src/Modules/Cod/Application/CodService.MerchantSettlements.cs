using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    // Transactional (this plan's Global Constraints): parent-row insert (MerchantSettlement) plus
    // one MerchantSettlementItem insert and one CODCollection status update per covered shipment.
    // cod.settle is org-wide (RBAC.md §4) — no CheckBranchAccessAsync call, same as
    // CreateFinanceVerificationAsync.
    public async Task<MerchantSettlementResponse> CreateMerchantSettlementAsync(CreateMerchantSettlementRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (!await _merchantLookupService.ExistsAsync(request.MerchantId, cancellationToken))
        {
            throw new CodValidationException("MerchantId does not refer to a merchant in this organization.");
        }
        if (request.PeriodTo < request.PeriodFrom)
        {
            throw new CodValidationException("PeriodTo must not be before PeriodFrom.");
        }

        // OrganizationId filter is required here — MerchantId alone is not enough, since a caller
        // could pass a MerchantId that happens to exist in another organization (or, if a merchant
        // id were ever reused/guessed) and fold that org's COD collections into this org's
        // settlement. See this task's brief for the recurring bug-class this guards against.
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.OrganizationId == organizationId
                        && c.MerchantId == request.MerchantId
                        && c.Status == CodCollectionStatus.FinanceVerified
                        && c.MerchantSettlementItemId == null
                        && c.CollectedAt != null && c.CollectedAt >= request.PeriodFrom && c.CollectedAt <= request.PeriodTo)
            .ToListAsync(cancellationToken);
        if (collections.Count == 0)
        {
            throw new CodValidationException("No finance-verified COD collections found for this merchant in this period.");
        }

        // Transactional (this plan's Global Constraints): In-memory provider does not support transactions,
        // so we check IsRelational() to gate transaction usage. Against Postgres (production), this always
        // opens a transaction with no fallback path. Against in-memory (testing only), transaction stays null.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var settlement = new MerchantSettlement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                MerchantId = request.MerchantId,
                PeriodFrom = request.PeriodFrom,
                PeriodTo = request.PeriodTo,
                Status = MerchantSettlementStatus.Created
            };
            _dbContext.Set<MerchantSettlement>().Add(settlement);

            var items = new List<MerchantSettlementItem>();
            foreach (var collection in collections)
            {
                // CODAmount is the amount actually collected (collection.CollectedAmount), not the
                // originally-quoted ExpectedAmount — the two are equal in the happy path but a
                // partial/short collection must settle against what was actually taken from the
                // customer, per COD-LIFECYCLE.md §3's shortage/overage visibility principle.
                var codAmount = collection.CollectedAmount!.Value;
                var courierFee = collection.ShipmentTotalCharge;
                var item = new MerchantSettlementItem
                {
                    Id = Guid.NewGuid(),
                    MerchantSettlementId = settlement.Id,
                    ShipmentId = collection.ShipmentId,
                    CODAmount = codAmount,
                    CourierFee = courierFee,
                    OtherDeductions = 0m,
                    NetPayable = codAmount - courierFee
                };
                items.Add(item);
                _dbContext.Set<MerchantSettlementItem>().Add(item);

                collection.MerchantSettlementItemId = item.Id;
                collection.Status = CodCollectionStatus.MerchantSettlementCreated;
            }
            settlement.TotalPayable = items.Sum(i => i.NetPayable);

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.merchant_settlement_created", "MerchantSettlement", settlement.Id.ToString()), cancellationToken);
            return ToMerchantSettlementResponse(settlement, items);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    // Transactional — parent-row status flip (MerchantSettlement) plus a bulk CODCollection status
    // update for every item it covers.
    public async Task<MerchantSettlementResponse?> SettleMerchantSettlementAsync(Guid merchantSettlementId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        var settlement = await _dbContext.Set<MerchantSettlement>()
            .FirstOrDefaultAsync(m => m.Id == merchantSettlementId && m.OrganizationId == organizationId, cancellationToken);
        if (settlement is null)
        {
            return null;
        }
        if (settlement.Status != MerchantSettlementStatus.Created)
        {
            throw new CodValidationException($"MerchantSettlement must be Created to settle, was {settlement.Status}.");
        }

        var items = await _dbContext.Set<MerchantSettlementItem>().Where(i => i.MerchantSettlementId == merchantSettlementId).ToListAsync(cancellationToken);
        var itemIds = items.Select(i => i.Id).ToArray();
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.OrganizationId == organizationId && c.MerchantSettlementItemId != null && itemIds.Contains(c.MerchantSettlementItemId.Value))
            .ToListAsync(cancellationToken);

        // Transactional (this plan's Global Constraints): In-memory provider does not support transactions,
        // so we check IsRelational() to gate transaction usage. Against Postgres (production), this always
        // opens a transaction with no fallback path. Against in-memory (testing only), transaction stays null.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            settlement.Status = MerchantSettlementStatus.Settled;
            settlement.SettledAt = DateTimeOffset.UtcNow;
            foreach (var collection in collections)
            {
                collection.Status = CodCollectionStatus.Settled;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.merchant_settlement_settled", "MerchantSettlement", settlement.Id.ToString()), cancellationToken);
            return ToMerchantSettlementResponse(settlement, items);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static MerchantSettlementResponse ToMerchantSettlementResponse(MerchantSettlement settlement, List<MerchantSettlementItem> items) => new(
        settlement.Id, settlement.MerchantId, settlement.PeriodFrom, settlement.PeriodTo, settlement.TotalPayable,
        settlement.Status, settlement.SettledAt,
        items.Select(i => new MerchantSettlementItemResponse(i.Id, i.ShipmentId, i.CODAmount, i.CourierFee, i.OtherDeductions, i.NetPayable)).ToList());
}
