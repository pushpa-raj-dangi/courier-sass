using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Billing.Tests;

public class InvoiceGenerationJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task RunAsync_CyclePastDueOnPaidPlan_GeneratesInvoiceAndAdvancesCycle()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var cycleStart = DateTimeOffset.UtcNow.AddMonths(-1);
        var cycleEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = cycleStart, CurrentCycleEnd = cycleEnd, CreatedAt = cycleStart, UpdatedAt = cycleStart };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();

        var job = new InvoiceGenerationJob(dbContext, new StaticOptionsMonitor<BillingOptions>(new BillingOptions()));
        await job.RunAsync();

        var invoice = dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.SubscriptionId == subscription.Id);
        Assert.Equal(2000m, invoice.AmountNprSnapshot);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);

        var updatedSubscription = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(cycleEnd, updatedSubscription.CurrentCycleStart);
    }

    [Fact]
    public async Task RunAsync_FreeCyclePastDue_AdvancesCycleWithNoInvoice()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Free", Slug = $"free-{Guid.NewGuid():N}", MonthlyPriceNpr = 0m, IsActive = true, IsDefault = true, CreatedAt = DateTimeOffset.UtcNow };
        var cycleEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = cycleEnd.AddMonths(-1), CurrentCycleEnd = cycleEnd, CreatedAt = cycleEnd, UpdatedAt = cycleEnd };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();

        var job = new InvoiceGenerationJob(dbContext, new StaticOptionsMonitor<BillingOptions>(new BillingOptions()));
        await job.RunAsync();

        Assert.False(dbContext.Set<Invoice>().IgnoreQueryFilters().Any(i => i.SubscriptionId == subscription.Id));
    }

    [Fact]
    public async Task RunAsync_CalledTwiceForSameCycle_DoesNotDoubleInvoice()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro2-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var cycleEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = cycleEnd.AddMonths(-1), CurrentCycleEnd = cycleEnd, CreatedAt = cycleEnd, UpdatedAt = cycleEnd };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new InvoiceGenerationJob(dbContext, new StaticOptionsMonitor<BillingOptions>(new BillingOptions()));
        await job.RunAsync();

        // Second run: the cycle already advanced past "now" by the first run, so there is
        // nothing due — this proves the job is safely re-runnable, not just idempotent within
        // a single overlapping window.
        await job.RunAsync();

        var count = dbContext.Set<Invoice>().IgnoreQueryFilters().Count(i => i.SubscriptionId == subscription.Id);
        Assert.Equal(1, count);
    }
}
