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
        private readonly string _rawPayload;
        public FakeGatewayClient(PaymentGateway gateway, bool verifySucceeds, string rawPayload = "{}") { Gateway = gateway; _verifySucceeds = verifySucceeds; _rawPayload = rawPayload; }
        public PaymentGateway Gateway { get; }
        public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayInitiationResult(new GatewayCheckoutInfo("https://fake-gateway.test/pay", new Dictionary<string, string>()), $"ref-{paymentId}"));
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayVerificationResult(_verifySucceeds, _verifySucceeds ? "TXN-FAKE" : null, _rawPayload));
    }

    // Simulates a naive (unbound) gateway client — succeeds for ANY callback regardless of which
    // payment it's actually confirming, used only to prove PaymentService.CompletePaymentAsync
    // still calls VerifyAsync with the values needed to bind the callback to the right payment
    // (the real binding logic itself lives in Khalti/EsewaGatewayClient and is covered by their
    // own tests) — here we assert the caller-supplied "wrong payment's reference" callback is
    // rejected by a gateway client that actually checks expectedGatewayReference.
    private sealed class BindingCheckingGatewayClient : IPaymentGatewayClient
    {
        public PaymentGateway Gateway { get; init; }
        public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayInitiationResult(new GatewayCheckoutInfo("https://fake-gateway.test/pay", new Dictionary<string, string>()), $"ref-{paymentId}"));
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
        {
            var suppliedReference = callbackParameters.TryGetValue("gatewayReference", out var value) ? value : null;
            var succeeded = suppliedReference is not null && suppliedReference == expectedGatewayReference;
            return Task.FromResult(new GatewayVerificationResult(succeeded, succeeded ? "TXN-FAKE" : null, "{}"));
        }
    }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Guid? ReactivatedOrganizationId { get; private set; }
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            ReactivatedOrganizationId = organizationId;
            return Task.CompletedTask;
        }
        public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    [Fact]
    public async Task InitiatePaymentAsync_PersistsGatewayReferenceReturnedByGatewayClient()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Esewa, true) }, new FakeOrganizationLookupService());

        await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/return");

        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);
        Assert.Equal($"ref-{payment.Id}", payment.GatewayReference);
    }

    // The regression test for the Critical cross-invoice payment-confirmation vulnerability:
    // an org creates two payments (a cheap one and an expensive one), and the callback for the
    // EXPENSIVE payment supplies the CHEAP payment's gateway reference. A naive (unbound)
    // verification would look up that reference, see it's genuinely valid, and confirm the
    // expensive payment for free. PaymentService must pass payment.GatewayReference through to
    // VerifyAsync so a binding-aware gateway client (like the real Esewa/KhaltiGatewayClient)
    // can reject this — proven here via BindingCheckingGatewayClient.
    [Fact]
    public async Task CompletePaymentAsync_CallbackCarriesDifferentPaymentsGatewayReference_IsRejected()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        // A single Subscription (org has a unique-per-org Subscription row) with TWO invoices at
        // different amounts — mirrors the exploit's "two invoices, cheap and expensive" setup.
        var (_, subscription, cheapInvoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var expensiveInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SubscriptionId = subscription.Id,
            PlanId = subscription.PlanId!.Value,
            PlanNameSnapshot = "Pro",
            AmountNprSnapshot = 50000m,
            CycleStart = DateTimeOffset.UtcNow.AddMonths(-1),
            CycleEnd = DateTimeOffset.UtcNow,
            Status = InvoiceStatus.Overdue,
            DueAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1)
        };
        dbContext.Set<Invoice>().Add(expensiveInvoice);
        await dbContext.SaveChangesAsync();

        var gatewayClient = new BindingCheckingGatewayClient { Gateway = PaymentGateway.Esewa };
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

        await service.InitiatePaymentAsync(cheapInvoice.Id, PaymentGateway.Esewa, "https://example.test/return");
        var cheapPayment = dbContext.Set<Payment>().Single(p => p.InvoiceId == cheapInvoice.Id);
        await service.InitiatePaymentAsync(expensiveInvoice.Id, PaymentGateway.Esewa, "https://example.test/return");
        var expensivePayment = dbContext.Set<Payment>().Single(p => p.InvoiceId == expensiveInvoice.Id);

        // Attacker supplies the CHEAP payment's own gateway reference in the callback for the
        // EXPENSIVE payment's id.
        var maliciousCallback = new Dictionary<string, string> { ["gatewayReference"] = cheapPayment.GatewayReference! };
        var result = await service.CompletePaymentAsync(expensivePayment.Id, maliciousCallback);

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentStatus.Failed, dbContext.Set<Payment>().Single(p => p.Id == expensivePayment.Id).Status);
        Assert.Equal(InvoiceStatus.Overdue, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == expensiveInvoice.Id).Status);

        // Sanity check: the SAME reference, supplied against the payment it actually belongs to,
        // is accepted — proves the rejection above is really about binding, not a broken fake.
        var legitimateCallback = new Dictionary<string, string> { ["gatewayReference"] = cheapPayment.GatewayReference! };
        var legitimateResult = await service.CompletePaymentAsync(cheapPayment.Id, legitimateCallback);
        Assert.True(legitimateResult.Succeeded);
    }

    [Fact]
    public async Task CompletePaymentAsync_NonJsonRawPayloadOnFailure_DoesNotThrowAndStoresValidJson()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Khalti, false, "not json at all") }, new FakeOrganizationLookupService());
        await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Khalti, "https://example.test/return");
        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);

        var result = await service.CompletePaymentAsync(payment.Id, new Dictionary<string, string>());

        Assert.False(result.Succeeded);
        var reloaded = dbContext.Set<Payment>().Single(p => p.Id == payment.Id);
        Assert.Equal(PaymentStatus.Failed, reloaded.Status);
        Assert.NotNull(reloaded.RawVerificationPayload);
        // Must be valid JSON — parsing it back must not throw (this is what would otherwise
        // fail at the jsonb column write inside SaveChangesAsync with Postgres error 22P02).
        using var document = System.Text.Json.JsonDocument.Parse(reloaded.RawVerificationPayload!);
        Assert.Equal("not json at all", document.RootElement.GetProperty("raw").GetString());
    }
}
