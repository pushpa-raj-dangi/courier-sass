using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    // Transactional (this plan's Global Constraints): covers a parent-row insert
    // (RiderCashSettlement) plus a bulk update of every CodCollected CODCollection row for this
    // rider — a crash between the two would leave collections stuck at CodCollected with no
    // RiderCashSettlement to point back to, silently losing the deposit record.
    public async Task<RiderCashSettlementResponse> CreateRiderSettlementAsync(CreateRiderSettlementRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (await _branchLookupService.GetZoneInfoAsync(request.BranchId, cancellationToken) is null)
        {
            throw new CodValidationException("BranchId does not refer to a branch in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "cod.reconcile", request.BranchId, cancellationToken);

        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.CollectedByRiderId == request.RiderId && c.BranchId == request.BranchId && c.Status == CodCollectionStatus.CodCollected)
            .ToListAsync(cancellationToken);
        if (collections.Count == 0)
        {
            throw new CodValidationException("No collected COD found for this rider at this branch.");
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
            var settlement = new RiderCashSettlement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                RiderId = request.RiderId,
                BranchId = request.BranchId,
                DeclaredAmount = request.DeclaredAmount,
                ShipmentCount = collections.Count,
                SettledAt = DateTimeOffset.UtcNow,
                ConfirmedByUserId = actingUserId
            };
            _dbContext.Set<RiderCashSettlement>().Add(settlement);

            foreach (var collection in collections)
            {
                collection.RiderCashSettlementId = settlement.Id;
                collection.Status = CodCollectionStatus.RiderDeposited;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.rider_settlement_created", "RiderCashSettlement", settlement.Id.ToString()), cancellationToken);
            return ToRiderSettlementResponse(settlement);
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

    private static RiderCashSettlementResponse ToRiderSettlementResponse(RiderCashSettlement settlement) => new(
        settlement.Id, settlement.RiderId, settlement.BranchId, settlement.DeclaredAmount, settlement.ShipmentCount,
        settlement.SettledAt, settlement.ConfirmedByUserId, settlement.BranchCashSettlementId);

    // Transactional, same reasoning as CreateRiderSettlementAsync — parent-row insert
    // (BranchCashSettlement) plus a bulk FK-stamp on every named RiderCashSettlement plus a bulk
    // status update on every CODCollection those rider settlements cover.
    public async Task<BranchCashSettlementResponse> CreateBranchSettlementAsync(CreateBranchSettlementRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (await _branchLookupService.GetZoneInfoAsync(request.BranchId, cancellationToken) is null)
        {
            throw new CodValidationException("BranchId does not refer to a branch in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "cod.reconcile", request.BranchId, cancellationToken);
        if (request.RiderCashSettlementIds.Count == 0)
        {
            throw new CodValidationException("At least one RiderCashSettlementId is required.");
        }

        var riderSettlements = await _dbContext.Set<RiderCashSettlement>()
            .Where(r => request.RiderCashSettlementIds.Contains(r.Id) && r.BranchId == request.BranchId && r.BranchCashSettlementId == null)
            .ToListAsync(cancellationToken);
        if (riderSettlements.Count != request.RiderCashSettlementIds.Count)
        {
            throw new CodValidationException("One or more RiderCashSettlementIds are invalid, belong to a different branch, or are already reconciled.");
        }

        var riderSettlementIds = riderSettlements.Select(r => r.Id).ToArray();
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.RiderCashSettlementId != null && riderSettlementIds.Contains(c.RiderCashSettlementId.Value) && c.Status == CodCollectionStatus.RiderDeposited)
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
            var settlement = new BranchCashSettlement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                BranchId = request.BranchId,
                ConfirmedAmount = request.ConfirmedAmount,
                ReconciledByUserId = actingUserId,
                ReconciledAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<BranchCashSettlement>().Add(settlement);

            foreach (var riderSettlement in riderSettlements)
            {
                riderSettlement.BranchCashSettlementId = settlement.Id;
            }
            foreach (var collection in collections)
            {
                collection.Status = CodCollectionStatus.BranchReconciled;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.branch_settlement_created", "BranchCashSettlement", settlement.Id.ToString()), cancellationToken);
            return ToBranchSettlementResponse(settlement, riderSettlementIds.ToList());
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

    private static BranchCashSettlementResponse ToBranchSettlementResponse(BranchCashSettlement settlement, List<Guid> riderCashSettlementIds) => new(
        settlement.Id, settlement.BranchId, settlement.ConfirmedAmount, settlement.ReconciledByUserId, settlement.ReconciledAt, riderCashSettlementIds);

    // Transactional, same reasoning as the two settlement methods above — parent-row insert
    // (FinancialTransaction) plus a bulk status update on every CODCollection the verified
    // BranchCashSettlement covers. cod.settle is org-wide (RBAC.md §4: FinanceManager holds it with
    // BranchScope.AllBranches) — no CheckBranchAccessAsync call here, matching how report endpoints
    // elsewhere in this codebase skip branch scoping too.
    public async Task<FinancialTransactionResponse> CreateFinanceVerificationAsync(CreateFinanceVerificationRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        var branchCashSettlementExists = await _dbContext.Set<BranchCashSettlement>().AnyAsync(b => b.Id == request.BranchCashSettlementId, cancellationToken);
        if (!branchCashSettlementExists)
        {
            throw new CodValidationException("BranchCashSettlementId was not found.");
        }
        var alreadyVerified = await _dbContext.Set<FinancialTransaction>()
            .AnyAsync(f => f.ReferenceEntityType == "BranchCashSettlement" && f.ReferenceEntityId == request.BranchCashSettlementId, cancellationToken);
        if (alreadyVerified)
        {
            throw new CodValidationException("This BranchCashSettlement has already been finance-verified.");
        }

        var riderSettlementIds = await _dbContext.Set<RiderCashSettlement>()
            .Where(r => r.BranchCashSettlementId == request.BranchCashSettlementId)
            .Select(r => r.Id)
            .ToArrayAsync(cancellationToken);
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.RiderCashSettlementId != null && riderSettlementIds.Contains(c.RiderCashSettlementId.Value) && c.Status == CodCollectionStatus.BranchReconciled)
            .ToListAsync(cancellationToken);
        if (collections.Count == 0)
        {
            throw new CodValidationException("No branch-reconciled COD collections found for this branch settlement.");
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
            var financialTransaction = new FinancialTransaction
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Type = FinancialTransactionType.CodVerification,
                Amount = request.Amount,
                ReferenceEntityType = "BranchCashSettlement",
                ReferenceEntityId = request.BranchCashSettlementId,
                VerifiedByUserId = actingUserId,
                VerifiedAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<FinancialTransaction>().Add(financialTransaction);

            foreach (var collection in collections)
            {
                collection.Status = CodCollectionStatus.FinanceVerified;
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique (ReferenceEntityType, ReferenceEntityId) index (Task 2) caught a
                // concurrent duplicate verification of the same BranchCashSettlement that raced past
                // the AnyAsync pre-check above — surfaced as a clean validation error, not a 500.
                // Proven by Task 14's Postgres concurrency test.
                throw new CodValidationException("This BranchCashSettlement has already been finance-verified.");
            }
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.finance_verified", "FinancialTransaction", financialTransaction.Id.ToString()), cancellationToken);
            return ToFinancialTransactionResponse(financialTransaction);
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

    private static FinancialTransactionResponse ToFinancialTransactionResponse(FinancialTransaction transaction) => new(
        transaction.Id, transaction.Type, transaction.Amount, transaction.ReferenceEntityType, transaction.ReferenceEntityId,
        transaction.VerifiedByUserId, transaction.VerifiedAt);
}
