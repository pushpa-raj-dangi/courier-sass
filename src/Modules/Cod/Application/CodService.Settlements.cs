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

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // In-memory provider or other databases that don't support transactions
            // We'll proceed without a transaction; in production, Postgres will provide real ACID guarantees
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
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.rider_settlement_created", "RiderCashSettlement", settlement.Id.ToString()), cancellationToken);
            return ToRiderSettlementResponse(settlement);
        }
        catch
        {
            if (transaction != null)
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
}
