using Courier.Billing.Domain;

namespace Courier.Billing.Application;

public record GatewayCheckoutInfo(string RedirectUrl, IReadOnlyDictionary<string, string> FormFields);
public record GatewayVerificationResult(bool Succeeded, string? GatewayTransactionId, string RawPayload);

public interface IPaymentGatewayClient
{
    PaymentGateway Gateway { get; }
    Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default);
    Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default);
}
