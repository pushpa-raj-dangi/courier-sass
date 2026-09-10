using Courier.Audit.Application;
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record CreatePlanRequest(string Name, string Slug, decimal MonthlyPriceNpr, int? MaxBranches, int? MaxStaffUsers, int? MaxMonthlyShipments, bool IsDefault);
public record PlanResponse(Guid Id, string Name, string Slug, decimal MonthlyPriceNpr, int? MaxBranches, int? MaxStaffUsers, int? MaxMonthlyShipments, bool IsDefault, bool IsActive);

public class PlanService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public PlanService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<PlanResponse> CreatePlanAsync(CreatePlanRequest request, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        var slugTaken = await _dbContext.Set<Plan>().AnyAsync(p => p.Slug == request.Slug, cancellationToken);
        if (slugTaken)
        {
            throw new BillingValidationException($"Plan slug '{request.Slug}' is already in use.");
        }
        if (request.MonthlyPriceNpr < 0)
        {
            throw new BillingValidationException("Monthly price cannot be negative.");
        }

        if (request.IsDefault)
        {
            var existingDefaults = await _dbContext.Set<Plan>().Where(p => p.IsDefault).ToListAsync(cancellationToken);
            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = request.Slug,
            MonthlyPriceNpr = request.MonthlyPriceNpr,
            MaxBranches = request.MaxBranches,
            MaxStaffUsers = request.MaxStaffUsers,
            MaxMonthlyShipments = request.MaxMonthlyShipments,
            IsDefault = request.IsDefault,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Set<Plan>().Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(null, actingSuperAdminId, "plan.created", "Plan", plan.Id.ToString(), IsCrossTenantAction: true), cancellationToken);

        return ToResponse(plan);
    }

    public async Task<List<PlanResponse>> ListPlansAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Set<Plan>().OrderBy(p => p.MonthlyPriceNpr).Select(p => ToResponse(p)).ToListAsync(cancellationToken);

    // Staff-facing subset of ListPlansAsync (which is SuperAdmin-only and includes retired
    // plans) — an OrganizationOwner choosing a plan must never be offered a retired one.
    public async Task<List<PlanResponse>> ListActivePlansAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Set<Plan>().Where(p => p.IsActive).OrderBy(p => p.MonthlyPriceNpr).Select(p => ToResponse(p)).ToListAsync(cancellationToken);

    public async Task RetirePlanAsync(Guid planId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Set<Plan>().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new BillingValidationException("Plan was not found.");
        if (plan.IsDefault)
        {
            throw new BillingValidationException("Cannot retire the default plan — assign a new default first.");
        }
        plan.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(null, actingSuperAdminId, "plan.retired", "Plan", planId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    private static PlanResponse ToResponse(Plan p)
        => new(p.Id, p.Name, p.Slug, p.MonthlyPriceNpr, p.MaxBranches, p.MaxStaffUsers, p.MaxMonthlyShipments, p.IsDefault, p.IsActive);
}
