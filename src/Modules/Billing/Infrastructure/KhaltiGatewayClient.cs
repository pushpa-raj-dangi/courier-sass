using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Infrastructure;

// Khalti KPG-2: initiate returns a ready-to-redirect payment_url (unlike eSewa's signed-form
// flow) keyed by an opaque "pidx"; confirmation is a separate server-to-server lookup call on
// that pidx — the browser-side return-URL query params are only used to know which pidx to
// look up, never trusted for the actual paid/not-paid determination.
public class KhaltiGatewayClient : IPaymentGatewayClient
{
    private readonly KhaltiOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentGateway Gateway => PaymentGateway.Khalti;

    public KhaltiGatewayClient(IOptions<KhaltiOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("Khalti");
    }

    public async Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
    {
        var amountPaisa = (long)(amountNpr * 100);
        var requestBody = new
        {
            return_url = returnUrl,
            website_url = _options.WebsiteUrl,
            amount = amountPaisa,
            purchase_order_id = paymentId.ToString(),
            purchase_order_name = $"Subscription invoice {paymentId}"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.InitiateUrl)
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", _options.SecretKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var paymentUrl = document.RootElement.GetProperty("payment_url").GetString()!;
        var pidx = document.RootElement.GetProperty("pidx").GetString();

        // pidx is persisted as Payment.GatewayReference so VerifyAsync can bind a later callback
        // back to THIS payment — a callback carrying a different (even genuinely "Completed")
        // pidx must never be accepted for this payment.
        return new GatewayInitiationResult(new GatewayCheckoutInfo(paymentUrl, new Dictionary<string, string>()), pidx);
    }

    public async Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        if (!callbackParameters.TryGetValue("pidx", out var pidx))
        {
            return new GatewayVerificationResult(false, null, "{}");
        }

        // Reject fast, before ever calling Khalti's lookup API, if the callback's pidx doesn't
        // belong to this payment — closes the cross-payment confirmation vulnerability where a
        // caller supplies a DIFFERENT (genuinely completed) payment's pidx to confirm this one.
        if (expectedGatewayReference is not null && pidx != expectedGatewayReference)
        {
            return new GatewayVerificationResult(false, null, "{}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.LookupUrl)
        {
            Content = JsonContent.Create(new { pidx })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", _options.SecretKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new GatewayVerificationResult(false, null, body);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var status = document.RootElement.GetProperty("status").GetString();
            var transactionId = document.RootElement.TryGetProperty("transaction_id", out var txProp) ? txProp.GetString() : null;
            var totalAmountPaisa = document.RootElement.TryGetProperty("total_amount", out var amountProp) ? amountProp.GetInt64() : (long?)null;
            var expectedAmountPaisa = (long)(expectedAmountNpr * 100);

            var succeeded = status == "Completed" && totalAmountPaisa == expectedAmountPaisa;
            return new GatewayVerificationResult(succeeded, transactionId, body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Malformed lookup response (missing/non-string "status") must never propagate as
            // an unhandled exception — treat it as an unverified payment.
            return new GatewayVerificationResult(false, null, body);
        }
    }
}
