using Courier.Billing.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class PlanServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task CreatePlanAsync_ValidRequest_PersistsAndReturnsPlan()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext);
        var slug = $"test-plan-{Guid.NewGuid():N}";

        var result = await service.CreatePlanAsync(new CreatePlanRequest("Test Plan", slug, 999m, 5, 10, 1000, false));

        Assert.Equal(slug, result.Slug);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task CreatePlanAsync_DuplicateSlug_Throws()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext);
        var slug = $"dup-plan-{Guid.NewGuid():N}";
        await service.CreatePlanAsync(new CreatePlanRequest("First", slug, 0m, null, null, null, false));

        await Assert.ThrowsAsync<BillingValidationException>(() =>
            service.CreatePlanAsync(new CreatePlanRequest("Second", slug, 0m, null, null, null, false)));
    }

    [Fact]
    public async Task CreatePlanAsync_NewDefault_ClearsPreviousDefault()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext);
        var firstSlug = $"first-default-{Guid.NewGuid():N}";
        var secondSlug = $"second-default-{Guid.NewGuid():N}";
        var first = await service.CreatePlanAsync(new CreatePlanRequest("Free", firstSlug, 0m, null, null, null, true));

        await service.CreatePlanAsync(new CreatePlanRequest("Free v2", secondSlug, 0m, null, null, null, true));

        var plans = await service.ListPlansAsync();
        Assert.False(plans.Single(p => p.Id == first.Id).IsDefault);
    }
}
