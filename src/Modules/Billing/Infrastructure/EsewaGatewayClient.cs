using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Infrastructure;

// eSewa ePay v2: checkout is a signed HTML form POST (not a JSON-returned redirect URL like
// Khalti), and confirmation is a two-step check — decode+verify the signature on the
// browser's own return, THEN independently confirm via the server-to-server status-check API
// (VerifyAsync does both; the signature check alone is never treated as sufficient, since a
// client-side redirect can be replayed/forged).
public class EsewaGatewayClient : IPaymentGatewayClient
{
    private readonly EsewaOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentGateway Gateway => PaymentGateway.Esewa;

    public EsewaGatewayClient(IOptions<EsewaOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("Esewa");
    }

    public Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
    {
        var totalAmount = amountNpr.ToString("F2");
        var transactionUuid = paymentId.ToString();
        var signedFieldNames = "total_amount,transaction_uuid,product_code";
        var signature = SignFields(($"total_amount={totalAmount},transaction_uuid={transactionUuid},product_code={_options.ProductCode}"));

        var fields = new Dictionary<string, string>
        {
            ["amount"] = totalAmount,
            ["tax_amount"] = "0",
            ["total_amount"] = totalAmount,
            ["transaction_uuid"] = transactionUuid,
            ["product_code"] = _options.ProductCode,
            ["product_service_charge"] = "0",
            ["product_delivery_charge"] = "0",
            ["success_url"] = returnUrl,
            ["failure_url"] = returnUrl,
            ["signed_field_names"] = signedFieldNames,
            ["signature"] = signature
        };

        return Task.FromResult(new GatewayCheckoutInfo(_options.FormUrl, fields));
    }

    public async Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        if (!callbackParameters.TryGetValue("data", out var encodedData))
        {
            return new GatewayVerificationResult(false, null, "{}");
        }

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedData));
        using var document = JsonDocument.Parse(decoded);
        var root = document.RootElement;
        var totalAmount = root.GetProperty("total_amount").GetString() ?? "";
        var transactionUuid = root.GetProperty("transaction_uuid").GetString() ?? "";
        var receivedSignature = root.GetProperty("signature").GetString() ?? "";

        var expectedSignature = SignFields($"total_amount={totalAmount},transaction_uuid={transactionUuid},product_code={_options.ProductCode}");
        if (receivedSignature != expectedSignature || transactionUuid != paymentId.ToString())
        {
            return new GatewayVerificationResult(false, null, decoded);
        }

        // Server-to-server confirmation — never trust the decoded redirect payload alone.
        var statusUrl = $"{_options.StatusCheckUrl}?product_code={Uri.EscapeDataString(_options.ProductCode)}&total_amount={Uri.EscapeDataString(totalAmount)}&transaction_uuid={Uri.EscapeDataString(transactionUuid)}";
        var response = await _httpClient.GetAsync(statusUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new GatewayVerificationResult(false, null, body);
        }

        using var statusDocument = JsonDocument.Parse(body);
        var status = statusDocument.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        var transactionCode = root.TryGetProperty("transaction_code", out var codeProp) ? codeProp.GetString() : null;

        return new GatewayVerificationResult(status == "COMPLETE", transactionCode, body);
    }

    private string SignFields(string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = new HMACSHA256(keyBytes).ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }
}
