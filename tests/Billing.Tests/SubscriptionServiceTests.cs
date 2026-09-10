using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class SubscriptionServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid? organizationId) { OrganizationId = organizationId; }
        public Guid? OrganizationId { get; }
    }

    // Platform (OrganizationLifecycleService/PlatformDashboardService) always calls
    // GetPlanSummaryAsync/ListInvoicesAsync with a null ambient tenant — a real EF-backed test
    // using this provider is the only way to catch a missing IgnoreQueryFilters() here, since a
    // hand-written fake IBillingLookupService (as Platform.Tests uses) can't exercise
    // AppDbContext's actual fail-closed query filter behavior.
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private static async Task<Plan> SeedPlanAsync(Courier.Persistence.AppDbContext dbContext, int? maxBranches = null, decimal price = 0m)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Test", Slug = $"plan-{Guid.NewGuid():N}", MonthlyPriceNpr = price, MaxBranches = maxBranches, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        await dbContext.SaveChangesAsync();
        return plan;
    }

    [Fact]
    public async Task AssignPlanAsync_NewOrganization_CreatesActiveSubscription()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext);
        var service = new SubscriptionService(dbContext);

        var result = await service.AssignPlanAsync(organizationId, plan.Id);

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.Equal(plan.Id, result.PlanId);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_PlanWithLimit_ReturnsLimit()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext, maxBranches: 3);
        var service = new SubscriptionService(dbContext);
        await service.AssignPlanAsync(organizationId, plan.Id);

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Equal(3, max);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_NoSubscription_ReturnsNullUnlimited()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = new SubscriptionService(dbContext);

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Null(max);
    }

    // Regression test for the Critical silent-data-empty bug: under Platform's real null ambient
    // tenant, GetPlanSummaryAsync must still return the org's actual plan summary, not null
    // (which the fail-closed Subscription/Plan query filters would otherwise force).
    [Fact]
    public async Task GetPlanSummaryAsync_WithNullAmbientTenant_ReturnsRealPlanSummary()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var service = new SubscriptionService(dbContext);

        var result = await service.GetPlanSummaryAsync(organizationId);

        Assert.NotNull(result);
        Assert.Equal("Growth", result!.PlanName);
        Assert.Equal(3000m, result.MonthlyPriceNpr);
        Assert.Equal(SubscriptionStatus.Active, result.Status);
    }

    // Regression test for the same bug in ListInvoicesAsync — the org-detail Sheet's Invoices
    // section is populated from this method under Platform's null ambient tenant.
    [Fact]
    public async Task ListInvoicesAsync_WithNullAmbientTenant_ReturnsRealInvoices()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Paid, DueAt = DateTimeOffset.UtcNow.AddDays(-1), PaidAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        var service = new SubscriptionService(dbContext);

        var result = await service.ListInvoicesAsync(organizationId);

        var singleInvoice = Assert.Single(result);
        Assert.Equal(invoice.Id, singleInvoice.Id);
        Assert.Equal(plan.Name, singleInvoice.PlanName);
    }
}
