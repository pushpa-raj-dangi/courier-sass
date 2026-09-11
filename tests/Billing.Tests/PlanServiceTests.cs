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

        // Search by the unique slug prefix (not an unfiltered ListPlansAsync()) — the shared test
        // DB accumulates plans across many tests/sessions, and an unfiltered default page could
        // miss this specific plan once the table exceeds one page.
        var plans = await service.ListPlansAsync(search: firstSlug);
        Assert.False(plans.Items.Single(p => p.Id == first.Id).IsDefault);
    }

    [Fact]
    public async Task ListPlansAsync_SearchMatchesNameSubstring()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var marker = Guid.NewGuid().ToString("N")[..8];
        await service.CreatePlanAsync(new CreatePlanRequest($"Growth {marker}", $"growth-{marker}", 3000m, null, null, null, false), Guid.NewGuid());
        await service.CreatePlanAsync(new CreatePlanRequest($"Starter {marker}", $"starter-{marker}", 0m, null, null, null, false), Guid.NewGuid());

        var result = await service.ListPlansAsync(search: $"growth {marker}");

        var single = Assert.Single(result.Items);
        Assert.Equal($"Growth {marker}", single.Name);
    }

    [Fact]
    public async Task ListPlansAsync_SearchMatchesSlugSubstring()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var marker = Guid.NewGuid().ToString("N")[..8];
        var created = await service.CreatePlanAsync(new CreatePlanRequest($"Plan {marker}", $"custom-slug-{marker}", 500m, null, null, null, false), Guid.NewGuid());

        var result = await service.ListPlansAsync(search: $"custom-slug-{marker}");

        Assert.Contains(result.Items, p => p.Id == created.Id);
    }

    [Fact]
    public async Task ListPlansAsync_Paginates()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var marker = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            await service.CreatePlanAsync(new CreatePlanRequest($"Paginated {marker} {i}", $"paginated-{marker}-{i}", 100m * i, null, null, null, false), Guid.NewGuid());
        }

        var page1 = await service.ListPlansAsync(search: marker, page: 1, pageSize: 2);
        var page2 = await service.ListPlansAsync(search: marker, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page2.Items.Count);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page1.TotalPages);
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
