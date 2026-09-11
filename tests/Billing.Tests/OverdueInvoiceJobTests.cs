using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Billing.Tests;

public class OverdueInvoiceJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public HashSet<Guid> SuspendedOrganizationIds { get; } = new();
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationRecord?>(null);
        public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationUserRecord>(new List<OrganizationUserRecord>(), 1, 25, 0));
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationUserRecord?>(null);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) { SuspendedOrganizationIds.Add(organizationId); return Task.CompletedTask; }
    }

    [Fact]
    public async Task RunAsync_InvoicePastDueDate_MarksOverdueAndSuspendsOrganization()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow.AddDays(-8), Status = InvoiceStatus.Pending, DueAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        var organizationLookup = new FakeOrganizationLookupService();
        var job = new OverdueInvoiceJob(dbContext, organizationLookup);

        await job.RunAsync();

        Assert.Equal(InvoiceStatus.Overdue, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.Equal(SubscriptionStatus.PastDue, dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id).Status);
        Assert.Contains(organizationId, organizationLookup.SuspendedOrganizationIds);
    }

    [Fact]
    public async Task RunAsync_InvoiceNotYetDue_LeavesUnchanged()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro2-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow, CycleEnd = DateTimeOffset.UtcNow.AddMonths(1), Status = InvoiceStatus.Pending, DueAt = DateTimeOffset.UtcNow.AddDays(6), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        var organizationLookup = new FakeOrganizationLookupService();
        var job = new OverdueInvoiceJob(dbContext, organizationLookup);

        await job.RunAsync();

        Assert.Equal(InvoiceStatus.Pending, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.DoesNotContain(organizationId, organizationLookup.SuspendedOrganizationIds);
    }

    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid? organizationId) { OrganizationId = organizationId; }
        public Guid? OrganizationId { get; }
    }

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Finding 1 (Critical, final whole-branch review) end-to-end regression: converting a
    // lapsed trial (CurrentCycleEnd 60 days in the past) via AssignPlanAsync must not leave the
    // org back-billed and auto-suspended by the very next nightly job run. Exercises the real
    // sequence: SubscriptionService.AssignPlanAsync -> InvoiceGenerationJob -> OverdueInvoiceJob,
    // all against the same dbContext, following this file's existing job-test pattern.
    [Fact]
    public async Task LapsedTrialConversion_ThenNightlyJobs_StaysActiveWithNoBackDatedInvoice()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(new Subscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanId = null,
            Status = SubscriptionStatus.TrialExpired,
            TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-83),
            CurrentCycleStart = DateTimeOffset.UtcNow.AddDays(-90),
            CurrentCycleEnd = DateTimeOffset.UtcNow.AddDays(-60),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-90)
        });
        await dbContext.SaveChangesAsync();

        var subscriptionService = new SubscriptionService(dbContext, new NoopAuditLogger(), Options.Create(new BillingOptions()), new FixedTenantProvider(organizationId));
        await subscriptionService.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        var invoiceJob = new InvoiceGenerationJob(dbContext, Options.Create(new BillingOptions()));
        await invoiceJob.RunAsync();

        Assert.False(dbContext.Set<Invoice>().IgnoreQueryFilters().Any(i => i.OrganizationId == organizationId));

        var organizationLookup = new FakeOrganizationLookupService();
        var overdueJob = new OverdueInvoiceJob(dbContext, organizationLookup);
        await overdueJob.RunAsync();

        Assert.DoesNotContain(organizationId, organizationLookup.SuspendedOrganizationIds);
        Assert.Equal(SubscriptionStatus.Active, dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.OrganizationId == organizationId).Status);
    }
}
