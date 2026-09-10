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

    public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
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

        // eSewa's own transaction_uuid is always paymentId.ToString() — that's exactly what we
        // persist as GatewayReference so VerifyAsync's binding check below has a persisted value
        // to compare against, same as Khalti's opaque pidx.
        return Task.FromResult(new GatewayInitiationResult(new GatewayCheckoutInfo(_options.FormUrl, fields), transactionUuid));
    }

    public async Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        if (!callbackParameters.TryGetValue("data", out var encodedData))
        {
            return new GatewayVerificationResult(false, null, "{}");
        }

        string decoded;
        string totalAmount;
        string transactionUuid;
        string receivedSignature;
        string? transactionCode;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedData));
            using var document = JsonDocument.Parse(decoded);
            var root = document.RootElement;
            totalAmount = root.GetProperty("total_amount").GetString() ?? "";
            transactionUuid = root.GetProperty("transaction_uuid").GetString() ?? "";
            receivedSignature = root.GetProperty("signature").GetString() ?? "";
            transactionCode = root.TryGetProperty("transaction_code", out var codeProp) ? codeProp.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Malformed/attacker-crafted callback data (bad base64, invalid JSON, missing
            // fields) must never propagate as an unhandled exception — treat it as an
            // unverified payment.
            return new GatewayVerificationResult(false, null, "invalid payload");
        }

        var expectedSignature = SignFields($"total_amount={totalAmount},transaction_uuid={transactionUuid},product_code={_options.ProductCode}");
        if (receivedSignature != expectedSignature || transactionUuid != paymentId.ToString())
        {
            return new GatewayVerificationResult(false, null, decoded);
        }
        // Belt-and-suspenders binding check: for eSewa, transactionUuid == paymentId.ToString()
        // is already enforced above, and expectedGatewayReference is always set to that same
        // value at initiate time, so this can only fire if the persisted GatewayReference was
        // somehow tampered with or never set — never remove the paymentId.ToString() check above
        // in favor of this one.
        if (expectedGatewayReference is not null && transactionUuid != expectedGatewayReference)
        {
            return new GatewayVerificationResult(false, null, decoded);
        }
        if (totalAmount != expectedAmountNpr.ToString("F2"))
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
