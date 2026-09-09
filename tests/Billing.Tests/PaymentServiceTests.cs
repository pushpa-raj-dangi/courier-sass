using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Billing.Tests;

public class PaymentServiceTests
{
    // AppDbContext's tenant query filters (Invoice included) fail closed when OrganizationId is
    // null (see AppDbContext.cs), so InitiatePaymentAsync/CompletePaymentAsync — which query
    // through the ambient tenant-filtered DbSet<Invoice> — need a tenant context matching the
    // seeded organization, same convention as SubscriptionServiceTests' FixedTenantProvider.
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid? organizationId) { OrganizationId = organizationId; }
        public Guid? OrganizationId { get; }
    }

    private sealed class FakeGatewayClient : IPaymentGatewayClient
    {
        private readonly bool _verifySucceeds;
        public FakeGatewayClient(PaymentGateway gateway, bool verifySucceeds) { Gateway = gateway; _verifySucceeds = verifySucceeds; }
        public PaymentGateway Gateway { get; }
        public Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayCheckoutInfo("https://fake-gateway.test/pay", new Dictionary<string, string>()));
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayVerificationResult(_verifySucceeds, _verifySucceeds ? "TXN-FAKE" : null, "{}"));
    }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Guid? ReactivatedOrganizationId { get; private set; }
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            ReactivatedOrganizationId = organizationId;
            return Task.CompletedTask;
        }
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationRecord>());
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(Plan Plan, Subscription Subscription, Invoice Invoice)> SeedInvoiceAsync(Courier.Persistence.AppDbContext dbContext, Guid organizationId, SubscriptionStatus subscriptionStatus = SubscriptionStatus.PastDue)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = subscriptionStatus, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Overdue, DueAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        return (plan, subscription, invoice);
    }

    [Fact]
    public async Task CompletePaymentAsync_GatewayConfirms_MarksPaidAndReactivatesOrg()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var organizationLookup = new FakeOrganizationLookupService();
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Esewa, true) }, organizationLookup);
        var checkoutInfo = await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/return");
        Assert.NotNull(checkoutInfo);
        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);

        var result = await service.CompletePaymentAsync(payment.Id, new Dictionary<string, string>());

        Assert.True(result.Succeeded);
        Assert.Equal(InvoiceStatus.Paid, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.Equal(organizationId, organizationLookup.ReactivatedOrganizationId);
    }

    [Fact]
    public async Task CompletePaymentAsync_GatewayDenies_MarksFailedAndLeavesInvoiceUnpaid()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Khalti, false) }, new FakeOrganizationLookupService());
        await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Khalti, "https://example.test/return");
        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);

        var result = await service.CompletePaymentAsync(payment.Id, new Dictionary<string, string>());

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentStatus.Failed, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
        Assert.Equal(InvoiceStatus.Overdue, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
    }

    [Fact]
    public async Task InitiatePaymentAsync_AlreadyPaidInvoice_Throws()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        invoice.Status = InvoiceStatus.Paid;
        await dbContext.SaveChangesAsync();
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Esewa, true) }, new FakeOrganizationLookupService());

        await Assert.ThrowsAsync<BillingValidationException>(() => service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/return"));
    }
}
