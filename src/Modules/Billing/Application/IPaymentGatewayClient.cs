using Courier.Billing.Domain;

namespace Courier.Billing.Application;

public record GatewayCheckoutInfo(string RedirectUrl, IReadOnlyDictionary<string, string> FormFields);
public record GatewayVerificationResult(bool Succeeded, string? GatewayTransactionId, string RawPayload);
// GatewayReference is the gateway-side correlation id captured at initiate time (Khalti's pidx,
// eSewa's transaction_uuid) — persisted on Payment so VerifyAsync can bind a later callback back
// to the specific payment it claims to confirm. Deliberately NOT part of GatewayCheckoutInfo:
// GatewayCheckoutInfo is returned verbatim as the HTTP response body from the pay endpoint, and
// this value must stay server-side only.
public record GatewayInitiationResult(GatewayCheckoutInfo CheckoutInfo, string? GatewayReference);

public interface IPaymentGatewayClient
{
    PaymentGateway Gateway { get; }
    Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default);
    Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default);
    // Server-to-server-only confirmation — no signed browser callback payload required, only the
    // GatewayReference persisted at initiate time. Used by PaymentService.ReconcilePendingAsync to
    // resolve payments where the customer completed checkout on the gateway's own page but the
    // browser never made it back to /billing/callback (closed tab, dropped connection, etc.).
    Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default);
}
