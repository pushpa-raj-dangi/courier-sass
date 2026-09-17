namespace Courier.Billing.Application;

// Hangfire recurring job wrapper around PaymentService.ReconcilePendingAsync — see that method
// for why this exists (a customer can pay on eSewa/Khalti's own page and never return to
// /billing/callback, leaving the Payment stuck at Initiated forever). All the actual logic lives
// in PaymentService so CompletePaymentAsync (the real callback path) and this job share one
// "verification succeeded -> mark paid/reactivate" code path, same as InvoiceGenerationJob/
// OverdueInvoiceJob/TrialExpiryJob keeping their own logic in the job class since they have no
// equivalent request-driven counterpart to share it with.
public class PaymentReconciliationJob
{
    private readonly PaymentService _paymentService;

    public PaymentReconciliationJob(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Task RunAsync(CancellationToken cancellationToken = default) => _paymentService.ReconcilePendingAsync(cancellationToken);
}
