namespace Courier.Billing.Domain;

public class Payment
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public PaymentGateway Gateway { get; set; }
    public string? GatewayTransactionId { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal AmountNpr { get; set; }
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    // The gateway's own verification-call response, kept for audit/dispute resolution — never
    // parsed back out of this column at runtime, write-once.
    public string? RawVerificationPayload { get; set; }
    // The gateway-side correlation id (Khalti's pidx, or eSewa's transaction_uuid — which is
    // just Id.ToString()) persisted at INITIATE time, before any callback can possibly arrive.
    // VerifyAsync binds the callback's own correlation id back against this value so a caller
    // cannot supply a DIFFERENT payment's valid gateway reference to confirm THIS payment.
    public string? GatewayReference { get; set; }
}
