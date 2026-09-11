using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.Extensions.Options;
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

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static SubscriptionService CreateService(Courier.Persistence.AppDbContext dbContext, Courier.SharedKernel.ICurrentTenantProvider tenantProvider, FakeAuditLogger? auditLogger = null, int trialDays = 7) =>
        new(dbContext, auditLogger ?? new FakeAuditLogger(), Options.Create(new BillingOptions { TrialDays = trialDays }), tenantProvider);

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
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        var result = await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.Equal(plan.Id, result.PlanId);
    }

    [Fact]
    public async Task AssignPlanAsync_LogsAuditEntry()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext);
        var auditLogger = new FakeAuditLogger();
        var actingUserId = Guid.NewGuid();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        await service.AssignPlanAsync(organizationId, plan.Id, actingUserId);

        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("subscription.plan_assigned", entry.Action);
        Assert.Equal(actingUserId, entry.UserId);
        Assert.False(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task AssignPlanAsync_FromNullAmbientTenant_LogsCrossTenantAuditEntry()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = await SeedPlanAsync(dbContext);
        var auditLogger = new FakeAuditLogger();
        var service = CreateService(dbContext, new NullTenantProvider(), auditLogger);

        await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        var entry = Assert.Single(auditLogger.Entries);
        Assert.True(entry.IsCrossTenantAction);
    }

    // Finding 1 (Critical, final whole-branch review): a lapsed trial (CurrentCycleEnd already
    // in the past, e.g. converted 60 days after registering) must have its billing cycle reset
    // on conversion, or InvoiceGenerationJob/OverdueInvoiceJob back-bill and immediately suspend
    // the org that just paid to reactivate.
    [Fact]
    public async Task AssignPlanAsync_LapsedTrial_ResetsBillingCycleToNow()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext);
        var lapsedCycleEnd = DateTimeOffset.UtcNow.AddDays(-60);
        dbContext.Set<Subscription>().Add(new Subscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanId = null,
            Status = SubscriptionStatus.TrialExpired,
            TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-53),
            CurrentCycleStart = DateTimeOffset.UtcNow.AddDays(-90),
            CurrentCycleEnd = lapsedCycleEnd,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90)
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        var beforeCall = DateTimeOffset.UtcNow;

        var result = await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.True(result.CurrentCycleStart >= beforeCall && result.CurrentCycleStart <= DateTimeOffset.UtcNow);
        Assert.True(result.CurrentCycleEnd >= beforeCall.AddMonths(1) && result.CurrentCycleEnd <= DateTimeOffset.UtcNow.AddMonths(1));
    }

    // Guards the preserved mid-trial-conversion behavior (spec §6.2): converting before the
    // current cycle has elapsed must NOT reset the billing clock.
    [Fact]
    public async Task AssignPlanAsync_MidTrialConversion_DoesNotResetBillingCycle()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext);
        var originalCycleStart = DateTimeOffset.UtcNow.AddDays(-3);
        var originalCycleEnd = DateTimeOffset.UtcNow.AddDays(27);
        dbContext.Set<Subscription>().Add(new Subscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanId = null,
            Status = SubscriptionStatus.Trial,
            TrialEndsAt = DateTimeOffset.UtcNow.AddDays(4),
            CurrentCycleStart = originalCycleStart,
            CurrentCycleEnd = originalCycleEnd,
            CreatedAt = originalCycleStart
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        var result = await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.Equal(originalCycleStart, result.CurrentCycleStart);
        Assert.Equal(originalCycleEnd, result.CurrentCycleEnd);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_PlanWithLimit_ReturnsLimit()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext, maxBranches: 3);
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Equal(3, max);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_NoSubscription_ReturnsNullUnlimited()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Null(max);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_TrialWithNoPlan_ReturnsNullUnlimited()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        await service.StartTrialAsync(organizationId, Guid.NewGuid());

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
        var service = CreateService(dbContext, new NullTenantProvider());

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
        var service = CreateService(dbContext, new NullTenantProvider());

        var result = await service.ListInvoicesAsync(organizationId);

        var singleInvoice = Assert.Single(result);
        Assert.Equal(invoice.Id, singleInvoice.Id);
        Assert.Equal(plan.Name, singleInvoice.PlanName);
    }

    [Fact]
    public async Task ListInvoicesForOrganizationAsync_ScopesToOrganizationAndPaginates()
    {
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        var otherSubscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = otherOrganizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().AddRange(subscription, otherSubscription);
        for (var i = 0; i < 3; i++)
        {
            dbContext.Set<Invoice>().Add(new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-i - 1), CycleEnd = DateTimeOffset.UtcNow.AddMonths(-i), Status = InvoiceStatus.Paid, DueAt = DateTimeOffset.UtcNow.AddDays(-1), PaidAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        }
        dbContext.Set<Invoice>().Add(new Invoice { Id = Guid.NewGuid(), OrganizationId = otherOrganizationId, SubscriptionId = otherSubscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Paid, DueAt = DateTimeOffset.UtcNow.AddDays(-1), PaidAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new NullTenantProvider());

        var page1 = await service.ListInvoicesForOrganizationAsync(organizationId, page: 1, pageSize: 2);
        var page2 = await service.ListInvoicesForOrganizationAsync(organizationId, page: 2, pageSize: 2);

        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(3, page1.TotalCount);
        Assert.All(page1.Items.Concat(page2.Items), i => Assert.Equal(plan.Name, i.PlanName));
    }

    [Fact]
    public async Task StartTrialAsync_CreatesTrialSubscriptionWithNoPlan()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), trialDays: 7);
        var beforeCall = DateTimeOffset.UtcNow;

        await service.StartTrialAsync(organizationId, Guid.NewGuid());

        var subscription = dbContext.Set<Subscription>().Single(s => s.OrganizationId == organizationId);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Null(subscription.PlanId);
        Assert.NotNull(subscription.TrialEndsAt);
        Assert.True(subscription.TrialEndsAt >= beforeCall.AddDays(7) && subscription.TrialEndsAt <= DateTimeOffset.UtcNow.AddDays(7));
    }

    [Fact]
    public async Task StartTrialAsync_LogsAuditEntry()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var auditLogger = new FakeAuditLogger();
        var actingUserId = Guid.NewGuid();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        await service.StartTrialAsync(organizationId, actingUserId);

        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("subscription.trial_started", entry.Action);
        Assert.Equal(actingUserId, entry.UserId);
    }

    // Finding 5 (Important, final whole-branch review): StartTrialAsync's doc comment claims
    // "called once ... and never again" — nothing previously enforced that, so a second call
    // (a caller retry) hit the unique index on Subscriptions.OrganizationId and threw a raw
    // DbUpdateException. It must now be a silent no-op instead.
    [Fact]
    public async Task StartTrialAsync_CalledTwiceForSameOrganization_IsIdempotentNoOp()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var auditLogger = new FakeAuditLogger();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        await service.StartTrialAsync(organizationId, Guid.NewGuid());
        var exception = await Record.ExceptionAsync(() => service.StartTrialAsync(organizationId, Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Equal(1, dbContext.Set<Subscription>().Count(s => s.OrganizationId == organizationId));
        Assert.Single(auditLogger.Entries);
    }

    [Fact]
    public async Task IsWriteBlockedAsync_TrialExpiredStatus_ReturnsTrue()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        dbContext.Set<Subscription>().Add(new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, Status = SubscriptionStatus.TrialExpired, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1), CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.True(await service.IsWriteBlockedAsync(organizationId));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial)]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task IsWriteBlockedAsync_AnyOtherStatus_ReturnsFalse(SubscriptionStatus status)
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        dbContext.Set<Subscription>().Add(new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, Status = status, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.False(await service.IsWriteBlockedAsync(organizationId));
    }

    [Fact]
    public async Task IsWriteBlockedAsync_NoSubscription_ReturnsFalse()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.False(await service.IsWriteBlockedAsync(organizationId));
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_TrialWithNoPlan_ReturnsStatusWithNullPlanFields()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        await service.StartTrialAsync(organizationId, Guid.NewGuid());

        var result = await service.GetSubscriptionStatusAsync(organizationId);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionStatus.Trial, result!.Status);
        Assert.NotNull(result.TrialEndsAt);
        Assert.Null(result.PlanName);
        Assert.Null(result.MonthlyPriceNpr);
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_WithNullAmbientTenant_ReturnsRealStatus()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new NullTenantProvider());

        var result = await service.GetSubscriptionStatusAsync(organizationId);

        Assert.NotNull(result);
        Assert.Equal("Growth", result!.PlanName);
        Assert.Equal(3000m, result.MonthlyPriceNpr);
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_NoSubscription_ReturnsNull()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.Null(await service.GetSubscriptionStatusAsync(organizationId));
    }
}
