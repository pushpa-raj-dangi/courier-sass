using System.Text.Json;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record PaymentCompletionResult(bool Succeeded, string Message);

public class PaymentService
{
    // Bounds for ReconcilePendingAsync below: a payment younger than MinPendingAge is likely
    // still mid-checkout on the gateway's own page, so re-polling it early would be wasted calls
    // (and could race the customer's own return to /billing/callback); one still Initiated past
    // MaxPendingAge is treated as abandoned rather than polled forever.
    private static readonly TimeSpan MinPendingAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxPendingAge = TimeSpan.FromHours(24);

    // A burst of abandoned checkouts (e.g. a gateway outage) could otherwise leave hundreds of
    // payments stuck at Initiated at once, and this job would fire that many synchronous
    // CheckStatusAsync calls at a live gateway back-to-back in a single run. Capping the batch
    // bounds that blast radius; oldest-first ordering means the next 10-minute run picks up
    // where this one left off rather than starving the oldest stuck payments.
    private const int MaxPaymentsPerRun = 25;

    private readonly DbContext _dbContext;
    private readonly IEnumerable<IPaymentGatewayClient> _gatewayClients;
    private readonly IOrganizationLookupService _organizationLookupService;

    public PaymentService(DbContext dbContext, IEnumerable<IPaymentGatewayClient> gatewayClients, IOrganizationLookupService organizationLookupService)
    {
        _dbContext = dbContext;
        _gatewayClients = gatewayClients;
        _organizationLookupService = organizationLookupService;
    }

    // returnBaseUrl is a bare page URL the frontend controls (e.g. "https://app.example.com/billing/callback"),
    // NOT the full return URL — the caller cannot know Payment.Id in advance (it's generated
    // here), so this method appends "/{payment.Id}" itself as a PATH segment, never a query
    // param, before handing the URL to the gateway. This also means the frontend never needs to
    // invent or track a correlation id of its own for "which payment is this browser tab waiting
    // on." Confirmed against the real eSewa RC sandbox: eSewa appends its own "?data=..." to
    // success_url using a literal "?", not "&", even when success_url already has a query string
    // — "?paymentId=X" then collided with eSewa's own "?data=Y" into one unparseable
    // "?paymentId=X?data=Y" blob, silently corrupting paymentId and dropping data entirely. A
    // path segment can never collide with a gateway's own query string, regardless of whether
    // that gateway appends it correctly with "&".
    public async Task<GatewayCheckoutInfo> InitiatePaymentAsync(Guid invoiceId, PaymentGateway gateway, string returnBaseUrl, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new BillingValidationException("Invoice was not found.");
        if (invoice.Status is not (InvoiceStatus.Pending or InvoiceStatus.Overdue))
        {
            throw new BillingValidationException($"Invoice is already {invoice.Status} and cannot be paid again.");
        }

        var client = _gatewayClients.FirstOrDefault(c => c.Gateway == gateway)
            ?? throw new BillingValidationException($"Gateway '{gateway}' is not configured.");

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Gateway = gateway,
            Status = PaymentStatus.Initiated,
            AmountNpr = invoice.AmountNprSnapshot,
            InitiatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Set<Payment>().Add(payment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var returnUrl = $"{returnBaseUrl.TrimEnd('/')}/{payment.Id}";
        var result = await client.InitiateAsync(payment.Id, invoice.AmountNprSnapshot, returnUrl, cancellationToken);

        // Second save after the external gateway call, same established pattern as
        // CompletePaymentAsync's own post-verification save and OverdueInvoiceJob's per-invoice
        // saves — persists the gateway reference BEFORE any callback for this payment can
        // possibly arrive, so CompletePaymentAsync always has something real to bind against.
        payment.GatewayReference = result.GatewayReference;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result.CheckoutInfo;
    }

    public async Task<PaymentCompletionResult> CompletePaymentAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        // Payment carries no OrganizationId of its own (Task 4 — isolates transitively through
        // InvoiceId -> Invoice.OrganizationId, same as ShipmentItem/ManifestShipment elsewhere in
        // this codebase). A raw Set<Payment>().FirstOrDefaultAsync(p => p.Id == paymentId) would
        // NOT be tenant-scoped at all (Payment has no query filter), letting any authenticated
        // caller in any org complete a payment by guessing another org's Payment id — exactly the
        // cross-tenant bug shape this project's own CLARIFICATIONS.md has hit repeatedly. Joining
        // through Invoice's own (filtered) DbSet is what actually enforces tenant scope here: a
        // cross-tenant paymentId's Invoice row is invisible under the caller's ambient filter, so
        // the join below returns nothing for it, exactly like a genuine 404.
        var payment = await (
            from p in _dbContext.Set<Payment>()
            join i in _dbContext.Set<Invoice>() on p.InvoiceId equals i.Id
            where p.Id == paymentId
            select p
        ).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BillingValidationException("Payment was not found.");
        if (payment.Status == PaymentStatus.Succeeded)
        {
            return new PaymentCompletionResult(true, "Already confirmed.");
        }

        var client = _gatewayClients.FirstOrDefault(c => c.Gateway == payment.Gateway)
            ?? throw new BillingValidationException($"Gateway '{payment.Gateway}' is not configured.");

        var verification = await client.VerifyAsync(payment.Id, payment.AmountNpr, payment.GatewayReference, callbackParameters, cancellationToken);

        if (!verification.Succeeded)
        {
            payment.Status = PaymentStatus.Failed;
            payment.RawVerificationPayload = ToJsonOrWrap(verification.RawPayload);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new PaymentCompletionResult(false, "Payment could not be confirmed. Please try again.");
        }

        await ApplySucceededVerificationAsync(payment, verification, cancellationToken);
        return new PaymentCompletionResult(true, "Payment confirmed.");
    }

    // Re-polls the gateway's own server-to-server status/lookup API (IPaymentGatewayClient.
    // CheckStatusAsync — no signed browser callback payload needed) for any Payment stuck at
    // Initiated, to catch customers who paid on the gateway's hosted page but whose browser
    // never made it back to /billing/callback. Called from PaymentReconciliationJob, a Hangfire
    // recurring job — same "no ambient tenant, IgnoreQueryFilters() everywhere" shape as
    // OverdueInvoiceJob/TrialExpiryJob, since there is no request-scoped tenant here at all.
    public async Task ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var freshCutoff = now - MinPendingAge;
        var abandonCutoff = now - MaxPendingAge;

        var stuckPayments = await _dbContext.Set<Payment>().IgnoreQueryFilters()
            .Where(p => p.Status == PaymentStatus.Initiated && p.GatewayReference != null && p.InitiatedAt <= freshCutoff)
            .OrderBy(p => p.InitiatedAt)
            .Take(MaxPaymentsPerRun)
            .ToListAsync(cancellationToken);

        foreach (var payment in stuckPayments)
        {
            var client = _gatewayClients.FirstOrDefault(c => c.Gateway == payment.Gateway);
            if (client is null)
            {
                continue;
            }

            var verification = await client.CheckStatusAsync(payment.Id, payment.AmountNpr, payment.GatewayReference, cancellationToken);
            if (verification.Succeeded)
            {
                await ApplySucceededVerificationAsync(payment, verification, cancellationToken);
            }
            else if (payment.InitiatedAt <= abandonCutoff)
            {
                // Past MaxPendingAge with still no confirmation — stop polling this one forever
                // and let it show up as a failed payment the merchant can retry.
                payment.Status = PaymentStatus.Failed;
                payment.RawVerificationPayload = ToJsonOrWrap(verification.RawPayload);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            // else: gateway still reports it unconfirmed and it's not abandoned yet — leave it
            // at Initiated for the next run to re-check.
        }
    }

    private async Task ApplySucceededVerificationAsync(Payment payment, GatewayVerificationResult verification, CancellationToken cancellationToken)
    {
        payment.RawVerificationPayload = ToJsonOrWrap(verification.RawPayload);
        payment.Status = PaymentStatus.Succeeded;
        payment.GatewayTransactionId = verification.GatewayTransactionId;
        payment.CompletedAt = DateTimeOffset.UtcNow;

        // IgnoreQueryFilters(): CompletePaymentAsync's caller runs inside the org's own tenant
        // context so the ambient filter would pass anyway, but ReconcilePendingAsync (the
        // Hangfire job path) has no ambient tenant at all — payment.InvoiceId/invoice.
        // SubscriptionId are already resolved from a specific, already-validated Payment row
        // either way, so bypassing the filter here cannot leak a different org's data.
        var invoice = await _dbContext.Set<Invoice>().IgnoreQueryFilters().FirstAsync(i => i.Id == payment.InvoiceId, cancellationToken);
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = payment.CompletedAt;

        var subscription = await _dbContext.Set<Subscription>().IgnoreQueryFilters().FirstAsync(s => s.Id == invoice.SubscriptionId, cancellationToken);
        if (subscription.Status == SubscriptionStatus.PastDue)
        {
            subscription.Status = SubscriptionStatus.Active;
            subscription.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Outside the SaveChangesAsync above deliberately — this touches a different module's
        // table (Organization) through its own lookup service, not a raw DbSet write, so it is
        // not part of Billing's own atomic unit of work. A crash between the two SaveChanges
        // calls leaves the invoice correctly marked Paid but the org still suspended until the
        // next manual check — an acceptable, narrow gap versus wrapping a cross-module call in
        // Billing's own transaction (this codebase has no cross-module distributed transaction
        // mechanism, and building one is far outside this phase's scope).
        await _organizationLookupService.ReactivateIfSystemSuspendedAsync(invoice.OrganizationId, cancellationToken);
    }

    // Payment.RawVerificationPayload is a jsonb column, but several gateway-client failure paths
    // return plain, non-JSON strings as RawPayload (e.g. eSewa's literal "invalid payload" on a
    // malformed callback, or either gateway's raw non-2xx HTTP response body, which could be an
    // HTML error page or empty string). Postgres rejects non-JSON input for a jsonb column at
    // SaveChangesAsync time with a 22P02 error — wrap any non-JSON value so the column write
    // always succeeds and the payment can still be marked Failed in a controlled way.
    private static string ToJsonOrWrap(string rawPayload)
    {
        try
        {
            using var _ = JsonDocument.Parse(rawPayload);
            return rawPayload;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { raw = rawPayload });
        }
    }
}
