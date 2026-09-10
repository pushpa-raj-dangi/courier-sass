using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class PlanServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task CreatePlanAsync_ValidRequest_PersistsAndReturnsPlan()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var slug = $"test-plan-{Guid.NewGuid():N}";

        var result = await service.CreatePlanAsync(new CreatePlanRequest("Test Plan", slug, 999m, 5, 10, 1000, false), Guid.NewGuid());

        Assert.Equal(slug, result.Slug);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task CreatePlanAsync_DuplicateSlug_Throws()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var slug = $"dup-plan-{Guid.NewGuid():N}";
        await service.CreatePlanAsync(new CreatePlanRequest("First", slug, 0m, null, null, null, false), Guid.NewGuid());

        await Assert.ThrowsAsync<BillingValidationException>(() =>
            service.CreatePlanAsync(new CreatePlanRequest("Second", slug, 0m, null, null, null, false), Guid.NewGuid()));
    }

    [Fact]
    public async Task CreatePlanAsync_NewDefault_ClearsPreviousDefault()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var firstSlug = $"first-default-{Guid.NewGuid():N}";
        var secondSlug = $"second-default-{Guid.NewGuid():N}";
        var first = await service.CreatePlanAsync(new CreatePlanRequest("Free", firstSlug, 0m, null, null, null, true), Guid.NewGuid());

        await service.CreatePlanAsync(new CreatePlanRequest("Free v2", secondSlug, 0m, null, null, null, true), Guid.NewGuid());

        var plans = await service.ListPlansAsync();
        Assert.False(plans.Single(p => p.Id == first.Id).IsDefault);
    }

    [Fact]
    public async Task CreatePlanAsync_WritesAuditEntry()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var auditLogger = new FakeAuditLogger();
        var service = new PlanService(dbContext, auditLogger);
        var actingSuperAdminId = Guid.NewGuid();
        var slug = $"audited-plan-{Guid.NewGuid():N}";

        var result = await service.CreatePlanAsync(new CreatePlanRequest("Audited Plan", slug, 500m, null, null, null, false), actingSuperAdminId);

        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("plan.created", entry.Action);
        Assert.Equal("Plan", entry.EntityType);
        Assert.Equal(result.Id.ToString(), entry.EntityId);
        Assert.Equal(actingSuperAdminId, entry.UserId);
        Assert.Null(entry.OrganizationId);
        Assert.True(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task RetirePlanAsync_WritesAuditEntry()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var auditLogger = new FakeAuditLogger();
        var service = new PlanService(dbContext, auditLogger);
        var slug = $"retire-plan-{Guid.NewGuid():N}";
        var plan = await service.CreatePlanAsync(new CreatePlanRequest("Retirable Plan", slug, 500m, null, null, null, false), Guid.NewGuid());
        var actingSuperAdminId = Guid.NewGuid();

        await service.RetirePlanAsync(plan.Id, actingSuperAdminId);

        var entry = Assert.Single(auditLogger.Entries, e => e.Action == "plan.retired");
        Assert.Equal("Plan", entry.EntityType);
        Assert.Equal(plan.Id.ToString(), entry.EntityId);
        Assert.Equal(actingSuperAdminId, entry.UserId);
        Assert.Null(entry.OrganizationId);
        Assert.True(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task ListActivePlansAsync_ExcludesRetiredPlans()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var activeSlug = $"active-{Guid.NewGuid():N}";
        var retiredSlug = $"retired-{Guid.NewGuid():N}";
        var active = await service.CreatePlanAsync(new CreatePlanRequest("Active Plan", activeSlug, 500m, null, null, null, false), Guid.NewGuid());
        var retired = await service.CreatePlanAsync(new CreatePlanRequest("Retired Plan", retiredSlug, 900m, null, null, null, false), Guid.NewGuid());
        await service.RetirePlanAsync(retired.Id, Guid.NewGuid());

        var result = await service.ListActivePlansAsync();

        Assert.Contains(result, p => p.Id == active.Id);
        Assert.DoesNotContain(result, p => p.Id == retired.Id);
    }
}
