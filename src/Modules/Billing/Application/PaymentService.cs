using Courier.Billing.Domain;
using Courier.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record PaymentCompletionResult(bool Succeeded, string Message);

public class PaymentService
{
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
    // here), so this method appends "?paymentId={payment.Id}" itself before handing the URL to
    // the gateway. This also means the frontend never needs to invent or track a correlation id
    // of its own for "which payment is this browser tab waiting on."
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

        var separator = returnBaseUrl.Contains('?') ? "&" : "?";
        var returnUrl = $"{returnBaseUrl}{separator}paymentId={payment.Id}";
        return await client.InitiateAsync(payment.Id, invoice.AmountNprSnapshot, returnUrl, cancellationToken);
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

        var verification = await client.VerifyAsync(payment.Id, callbackParameters, cancellationToken);
        payment.RawVerificationPayload = verification.RawPayload;

        if (!verification.Succeeded)
        {
            payment.Status = PaymentStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new PaymentCompletionResult(false, "Payment could not be confirmed. Please try again.");
        }

        payment.Status = PaymentStatus.Succeeded;
        payment.GatewayTransactionId = verification.GatewayTransactionId;
        payment.CompletedAt = DateTimeOffset.UtcNow;

        var invoice = await _dbContext.Set<Invoice>().FirstAsync(i => i.Id == payment.InvoiceId, cancellationToken);
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = payment.CompletedAt;

        var subscription = await _dbContext.Set<Subscription>().FirstAsync(s => s.Id == invoice.SubscriptionId, cancellationToken);
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

        return new PaymentCompletionResult(true, "Payment confirmed.");
    }
}
