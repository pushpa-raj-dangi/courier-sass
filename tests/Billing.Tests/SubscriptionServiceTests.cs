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
}
