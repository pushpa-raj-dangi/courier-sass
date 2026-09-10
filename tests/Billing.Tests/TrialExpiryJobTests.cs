using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Billing.Tests;

public class TrialExpiryJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task RunAsync_TrialPastEndDateWithNoPlan_FlipsToTrialExpired()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = null, Status = SubscriptionStatus.Trial, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1), CurrentCycleStart = DateTimeOffset.UtcNow.AddDays(-7), CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) };
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new TrialExpiryJob(dbContext);

        await job.RunAsync();

        var updated = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.TrialExpired, updated.Status);
    }

    [Fact]
    public async Task RunAsync_TrialNotYetEnded_LeavesUnchanged()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = null, Status = SubscriptionStatus.Trial, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(3), CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new TrialExpiryJob(dbContext);

        await job.RunAsync();

        var unchanged = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.Trial, unchanged.Status);
    }

    [Fact]
    public async Task RunAsync_TrialEndedButAlreadyOnRealPlan_LeavesUnchanged()
    {
        // A trial that already converted (AssignPlanAsync flipped it to Active) must never be
        // touched by this job even if its stale TrialEndsAt is in the past — only Status ==
        // Trial rows are candidates.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Converted", Slug = $"converted-{Guid.NewGuid():N}", MonthlyPriceNpr = 500m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1), CurrentCycleStart = DateTimeOffset.UtcNow.AddDays(-7), CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new TrialExpiryJob(dbContext);

        await job.RunAsync();

        var unchanged = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, unchanged.Status);
    }
}
