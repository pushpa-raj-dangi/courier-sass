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

    // ReconcilePendingAsync runs with no ambient tenant at all (same as the Hangfire jobs it sits
    // alongside — OverdueInvoiceJob/TrialExpiryJob), so its own tests seed directly via a
    // null-tenant dbContext rather than going through InitiatePaymentAsync's tenant-filtered path.
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeGatewayClient : IPaymentGatewayClient
    {
        private readonly bool _verifySucceeds;
        private readonly string _rawPayload;
        public FakeGatewayClient(PaymentGateway gateway, bool verifySucceeds, string rawPayload = "{}") { Gateway = gateway; _verifySucceeds = verifySucceeds; _rawPayload = rawPayload; }
        public PaymentGateway Gateway { get; }
        public string? CapturedReturnUrl { get; private set; }
        public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
        {
            CapturedReturnUrl = returnUrl;
            return Task.FromResult(new GatewayInitiationResult(new GatewayCheckoutInfo("https://fake-gateway.test/pay", new Dictionary<string, string>()), $"ref-{paymentId}"));
        }
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayVerificationResult(_verifySucceeds, _verifySucceeds ? "TXN-FAKE" : null, _rawPayload));
        public Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayVerificationResult(_verifySucceeds, _verifySucceeds ? "TXN-FAKE" : null, _rawPayload));
    }

    // Used only by ReconcilePendingAsync tests: CheckStatusAsync's result is configurable
    // per-call (unlike FakeGatewayClient's fixed constructor value) so a single test can assert
    // "still pending" now and "now confirmed" on a later run, and calls are counted so a test can
    // prove a too-fresh payment was skipped entirely rather than merely reported unconfirmed.
    // ReconcilePendingAsync's own query (IgnoreQueryFilters, "any Payment stuck at Initiated") is
    // deliberately broad — by design it runs with no ambient tenant, same as every other Hangfire
    // job in this codebase. TestDbContextFactory points at the same shared dev Postgres instance
    // these tests run against in this repo's convention (not an isolated per-test database), so a
    // stray Initiated Esewa payment left over from anything else (e.g. manual browser testing)
    // that happens to be old enough would ALSO be swept into that query here. targetPaymentId
    // scopes this fake's canned result to only the payment THIS test seeded — anything else
    // observed gets an inert "still pending" response rather than the test's configured result,
    // so a test can never accidentally mark an unrelated real payment Succeeded/Failed.
    private sealed class ReconciliationGatewayClient : IPaymentGatewayClient
    {
        private readonly Guid _targetPaymentId;
        private readonly Func<GatewayVerificationResult> _checkStatusResult;
        public ReconciliationGatewayClient(PaymentGateway gateway, Guid targetPaymentId, Func<GatewayVerificationResult> checkStatusResult) { Gateway = gateway; _targetPaymentId = targetPaymentId; _checkStatusResult = checkStatusResult; }
        public PaymentGateway Gateway { get; }
        public int CheckStatusCallCount { get; private set; }
        public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayInitiationResult(new GatewayCheckoutInfo("https://fake-gateway.test/pay", new Dictionary<string, string>()), $"ref-{paymentId}"));
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ReconcilePendingAsync must call CheckStatusAsync, never VerifyAsync (no browser callback payload exists to verify).");
        public Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default)
        {
            if (paymentId != _targetPaymentId)
            {
                return Task.FromResult(new GatewayVerificationResult(false, null, "{\"status\":\"PENDING\"}"));
            }
            CheckStatusCallCount++;
            return Task.FromResult(_checkStatusResult());
        }
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
        public Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayVerificationResult(false, null, "{}"));
    }

    // Used only by the batch-cap test below: unlike ReconciliationGatewayClient (scoped to one
    // target payment), this records every payment id it was asked to check so the test can
    // assert both "no more than the cap" and "oldest-first" without caring which specific
    // payments those are.
    private sealed class CallTrackingGatewayClient : IPaymentGatewayClient
    {
        private readonly List<Guid> _checkedPaymentIds = new();
        public PaymentGateway Gateway { get; init; }
        public IReadOnlyList<Guid> CheckedPaymentIds => _checkedPaymentIds;
        public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default)
        {
            _checkedPaymentIds.Add(paymentId);
            return Task.FromResult(new GatewayVerificationResult(false, null, "{\"status\":\"PENDING\"}"));
        }
    }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    // Regression test for a real bug caught against the live eSewa RC sandbox: eSewa appends its
    // own "?data=..." to success_url using a literal "?", not "&", even when success_url already
    // has a query string. A "?paymentId=X" query param collided with eSewa's own "?data=Y" into
    // one unparseable "?paymentId=X?data=Y" blob — corrupting paymentId and silently dropping
    // data, so /billing/callback could never confirm a real payment. paymentId must be a PATH
    // segment, which no gateway's own query-string append can ever collide with.
    [Fact]
    public async Task InitiatePaymentAsync_AppendsPaymentIdAsPathSegmentNotQueryParam()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var gatewayClient = new FakeGatewayClient(PaymentGateway.Esewa, true);
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

        await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/billing/callback");

        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);
        Assert.Equal($"https://example.test/billing/callback/{payment.Id}", gatewayClient.CapturedReturnUrl);
        Assert.DoesNotContain("?paymentId=", gatewayClient.CapturedReturnUrl);
    }

    [Fact]
    public async Task InitiatePaymentAsync_ReturnBaseUrlWithTrailingSlash_DoesNotProduceDoubleSlash()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var gatewayClient = new FakeGatewayClient(PaymentGateway.Esewa, true);
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

        await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/billing/callback/");

        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);
        Assert.Equal($"https://example.test/billing/callback/{payment.Id}", gatewayClient.CapturedReturnUrl);
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

    private static async Task<(Subscription Subscription, Invoice Invoice, Payment Payment)> SeedStuckPaymentAsync(
        Courier.Persistence.AppDbContext dbContext, Guid organizationId, DateTimeOffset initiatedAt, PaymentStatus status = PaymentStatus.Initiated, SubscriptionStatus subscriptionStatus = SubscriptionStatus.PastDue)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = subscriptionStatus, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = 2000m, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Overdue, DueAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        var payment = new Payment { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Gateway = PaymentGateway.Esewa, Status = status, AmountNpr = 2000m, InitiatedAt = initiatedAt, GatewayReference = Guid.NewGuid().ToString() };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        dbContext.Set<Payment>().Add(payment);
        await dbContext.SaveChangesAsync();
        return (subscription, invoice, payment);
    }

    [Fact]
    public async Task ReconcilePendingAsync_GatewayNowConfirms_MarksInvoicePaidAndReactivatesOrg()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var (_, invoice, payment) = await SeedStuckPaymentAsync(dbContext, organizationId, DateTimeOffset.UtcNow.AddMinutes(-10));
        var organizationLookup = new FakeOrganizationLookupService();
        var gatewayClient = new ReconciliationGatewayClient(PaymentGateway.Esewa, payment.Id, () => new GatewayVerificationResult(true, "REF-RECONCILED", "{}"));
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, organizationLookup);

        await service.ReconcilePendingAsync();

        Assert.Equal(PaymentStatus.Succeeded, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
        Assert.Equal(InvoiceStatus.Paid, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.Equal(organizationId, organizationLookup.ReactivatedOrganizationId);
    }

    [Fact]
    public async Task ReconcilePendingAsync_PaymentStillWithinGracePeriod_IsSkippedEntirely()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        // Initiated seconds ago — still well within the 5-minute grace period, so it must not
        // even be checked yet (the customer could still be mid-checkout on the gateway's page).
        var (_, _, payment) = await SeedStuckPaymentAsync(dbContext, organizationId, DateTimeOffset.UtcNow.AddSeconds(-30));
        var gatewayClient = new ReconciliationGatewayClient(PaymentGateway.Esewa, payment.Id, () => throw new InvalidOperationException("Should not have been called."));
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

        await service.ReconcilePendingAsync();

        Assert.Equal(0, gatewayClient.CheckStatusCallCount);
        Assert.Equal(PaymentStatus.Initiated, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
    }

    [Fact]
    public async Task ReconcilePendingAsync_GatewayStillPendingAndNotYetAbandoned_LeavesPaymentInitiated()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var (_, _, payment) = await SeedStuckPaymentAsync(dbContext, organizationId, DateTimeOffset.UtcNow.AddHours(-1));
        var gatewayClient = new ReconciliationGatewayClient(PaymentGateway.Esewa, payment.Id, () => new GatewayVerificationResult(false, null, "{\"status\":\"PENDING\"}"));
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

        await service.ReconcilePendingAsync();

        Assert.Equal(1, gatewayClient.CheckStatusCallCount);
        Assert.Equal(PaymentStatus.Initiated, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
    }

    [Fact]
    public async Task ReconcilePendingAsync_GatewayStillUnconfirmedPastMaxAge_MarksFailed()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var (_, _, payment) = await SeedStuckPaymentAsync(dbContext, organizationId, DateTimeOffset.UtcNow.AddHours(-25));
        var gatewayClient = new ReconciliationGatewayClient(PaymentGateway.Esewa, payment.Id, () => new GatewayVerificationResult(false, null, "{\"status\":\"PENDING\"}"));
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

        await service.ReconcilePendingAsync();

        Assert.Equal(PaymentStatus.Failed, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
    }

    [Fact]
    public async Task ReconcilePendingAsync_MoreStuckPaymentsThanBatchCap_ChecksOldestFirstUpToCap()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        const int seededCount = 30;
        const int batchCap = 25;
        // Dated years in the past (not just hours, like every other test in this file) so these
        // rows are guaranteed to sort before any real stray Initiated payment that might already
        // be sitting in the shared dev database (see the class-level comment on
        // ReconciliationGatewayClient) — otherwise this test's exact-count/exact-order assertions
        // below would be at the mercy of however much unrelated stray data happens to exist.
        var seeded = new List<Payment>();
        var createdIds = new List<Guid>();
        for (var i = 0; i < seededCount; i++)
        {
            // One organization per payment (Subscriptions has a unique index per OrganizationId) —
            // distinct InitiatedAt per payment (oldest = i=0) so oldest-first ordering is unambiguous.
            var (subscription, invoice, payment) = await SeedStuckPaymentAsync(dbContext, Guid.NewGuid(), DateTimeOffset.UtcNow.AddYears(-5).AddSeconds(-i));
            seeded.Add(payment);
            createdIds.Add(payment.Id);
        }
        try
        {
            var gatewayClient = new CallTrackingGatewayClient { Gateway = PaymentGateway.Esewa };
            var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { gatewayClient }, new FakeOrganizationLookupService());

            await service.ReconcilePendingAsync();

            Assert.Equal(batchCap, gatewayClient.CheckedPaymentIds.Count);
            var expectedOldestFirst = seeded.OrderBy(p => p.InitiatedAt).Take(batchCap).Select(p => p.Id);
            Assert.Equal(expectedOldestFirst, gatewayClient.CheckedPaymentIds);
        }
        finally
        {
            // This test alone seeds 30 rows straight into the shared dev database with no
            // automatic teardown (this file's established convention, see TestDbContextFactory) —
            // left behind, they'd both accumulate indefinitely across repeated local test runs
            // and get swept up by the real Hangfire reconciliation job hitting the live gateway.
            // Every other test in this file seeds only one payment and accepts that cost; this
            // one seeds 30, so it cleans up after itself.
            dbContext.Set<Payment>().RemoveRange(dbContext.Set<Payment>().Where(p => createdIds.Contains(p.Id)));
            await dbContext.SaveChangesAsync();
        }
    }
}
