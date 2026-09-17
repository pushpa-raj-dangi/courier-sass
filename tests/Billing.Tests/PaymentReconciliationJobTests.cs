using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Billing.Tests;

// PaymentReconciliationJob itself is a one-line Hangfire wrapper around PaymentService.
// ReconcilePendingAsync (see that method's own thorough coverage in PaymentServiceTests) — this
// file only proves the job actually delegates to it, same "thin job, real logic lives in the
// service" split as the rest of this file's siblings.
public class PaymentReconciliationJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public Guid? ReactivatedOrganizationId { get; private set; }
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) { ReactivatedOrganizationId = organizationId; return Task.CompletedTask; }
        public Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<OrganizationRecord>(new List<OrganizationRecord>(), 1, 25, 0));
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // TestDbContextFactory points at the same shared dev Postgres instance these tests run
    // against (this repo's existing convention, not an isolated per-test database) — and
    // ReconcilePendingAsync's query is deliberately broad (any Payment stuck at Initiated,
    // IgnoreQueryFilters, no ambient tenant). Scoping this stub's "confirmed" result to only the
    // payment THIS test seeded stops it from accidentally marking an unrelated stray real payment
    // as Succeeded if one happens to be sitting in the shared dev database when this test runs.
    private sealed class StubGatewayClient : IPaymentGatewayClient
    {
        private readonly Guid _targetPaymentId;
        public StubGatewayClient(Guid targetPaymentId) { _targetPaymentId = targetPaymentId; }
        public PaymentGateway Gateway => PaymentGateway.Esewa;
        public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default)
            => Task.FromResult(paymentId == _targetPaymentId
                ? new GatewayVerificationResult(true, "REF-JOB", "{}")
                : new GatewayVerificationResult(false, null, "{\"status\":\"PENDING\"}"));
    }

    [Fact]
    public async Task RunAsync_DelegatesToPaymentServiceReconcilePendingAsync()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.PastDue, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = 2000m, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Overdue, DueAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        var payment = new Payment { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Gateway = PaymentGateway.Esewa, Status = PaymentStatus.Initiated, AmountNpr = 2000m, InitiatedAt = DateTimeOffset.UtcNow.AddMinutes(-10), GatewayReference = Guid.NewGuid().ToString() };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        dbContext.Set<Payment>().Add(payment);
        await dbContext.SaveChangesAsync();

        var paymentService = new PaymentService(dbContext, new IPaymentGatewayClient[] { new StubGatewayClient(payment.Id) }, new FakeOrganizationLookupService());
        var job = new PaymentReconciliationJob(paymentService);

        await job.RunAsync();

        Assert.Equal(PaymentStatus.Succeeded, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
        Assert.Equal(InvoiceStatus.Paid, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
    }
}
