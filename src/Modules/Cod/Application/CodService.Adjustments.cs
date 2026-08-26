using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    // v1 supports exactly one EntityType — the only concrete adjustment case with a real business
    // need right now (this plan's Global Constraints / DOMAIN-MODEL.md's MerchantSettlementItem doc
    // comment). ApproveAdjustmentAsync's switch below is the single place this whitelist is
    // enforced on the write side too.
    private const string AdjustableEntityType = "MerchantSettlementItem";

    // Never touches the target row — only inserts the Adjustment record itself, unapplied. This
    // plan's resolved design decision: Adjustment always requires a separate approval step
    // (bounded brainstorm), so a single-row insert needs no transaction.
    public async Task<AdjustmentResponse> RequestAdjustmentAsync(RequestAdjustmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (request.EntityType != AdjustableEntityType)
        {
            throw new CodValidationException($"Adjustments are only supported for EntityType '{AdjustableEntityType}'.");
        }

        // MerchantSettlementItem has no own OrganizationId column — tenant isolation flows
        // transitively through MerchantSettlementId -> MerchantSettlement.OrganizationId, so the
        // scoping filter has to join through the parent settlement rather than filtering the item
        // directly. See this task's brief for the recurring bug-class this guards against.
        var item = await (
            from i in _dbContext.Set<MerchantSettlementItem>()
            join m in _dbContext.Set<MerchantSettlement>() on i.MerchantSettlementId equals m.Id
            where i.Id == request.EntityId && m.OrganizationId == organizationId
            select i
        ).FirstOrDefaultAsync(cancellationToken)
            ?? throw new CodValidationException("EntityId does not refer to a MerchantSettlementItem in this organization.");

        var adjustment = new Adjustment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            BeforeValue = item.OtherDeductions,
            AfterValue = request.AfterValue,
            Reason = request.Reason,
            RequestedByUserId = actingUserId,
            RequestedAt = DateTimeOffset.UtcNow,
            Applied = false
        };
        _dbContext.Set<Adjustment>().Add(adjustment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.adjustment_requested", "Adjustment", adjustment.Id.ToString()), cancellationToken);
        return ToAdjustmentResponse(adjustment);
    }

    // Transactional: rewrites MerchantSettlementItem.OtherDeductions/NetPayable, recomputes the
    // parent MerchantSettlement.TotalPayable, and flags the Adjustment Applied — three rows that
    // must move together or not at all, the one place outside the booking-time snapshot where a
    // financial figure is ever rewritten in place (CLAUDE.md §23), which is exactly why it demands
    // a transaction even though it is not part of the main status chain.
    public async Task<AdjustmentResponse?> ApproveAdjustmentAsync(Guid adjustmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        // Adjustment carries its own OrganizationId column, so this filter alone is sufficient —
        // no join required, unlike the MerchantSettlementItem lookup in RequestAdjustmentAsync.
        var adjustment = await _dbContext.Set<Adjustment>()
            .FirstOrDefaultAsync(a => a.Id == adjustmentId && a.OrganizationId == organizationId, cancellationToken);
        if (adjustment is null)
        {
            return null;
        }
        if (adjustment.Applied)
        {
            throw new CodValidationException("This adjustment has already been approved and applied.");
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
            switch (adjustment.EntityType)
            {
                case AdjustableEntityType:
                    // adjustment.EntityId was already verified to belong to this organization at
                    // RequestAdjustmentAsync time (via the MerchantSettlement join above), and the
                    // Adjustment row itself was just re-scoped by OrganizationId, so this lookup is
                    // transitively org-safe without repeating the join.
                    var item = await _dbContext.Set<MerchantSettlementItem>().FirstAsync(i => i.Id == adjustment.EntityId, cancellationToken);
                    item.OtherDeductions = adjustment.AfterValue;
                    item.NetPayable = item.CODAmount - item.CourierFee - item.OtherDeductions;

                    var settlement = await _dbContext.Set<MerchantSettlement>()
                        .FirstAsync(m => m.Id == item.MerchantSettlementId && m.OrganizationId == organizationId, cancellationToken);
                    settlement.TotalPayable = await _dbContext.Set<MerchantSettlementItem>()
                        .Where(i => i.MerchantSettlementId == settlement.Id)
                        .SumAsync(i => i.Id == item.Id ? item.NetPayable : i.NetPayable, cancellationToken);
                    break;

                default:
                    throw new CodValidationException($"Unsupported adjustment EntityType '{adjustment.EntityType}'.");
            }

            adjustment.ApprovedByUserId = actingUserId;
            adjustment.ApprovedAt = DateTimeOffset.UtcNow;
            adjustment.Applied = true;

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.adjustment_approved", "Adjustment", adjustment.Id.ToString()), cancellationToken);
            return ToAdjustmentResponse(adjustment);
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

    private static AdjustmentResponse ToAdjustmentResponse(Adjustment adjustment) => new(
        adjustment.Id, adjustment.EntityType, adjustment.EntityId, adjustment.BeforeValue, adjustment.AfterValue, adjustment.Reason,
        adjustment.RequestedByUserId, adjustment.RequestedAt, adjustment.ApprovedByUserId, adjustment.ApprovedAt, adjustment.Applied);
}
