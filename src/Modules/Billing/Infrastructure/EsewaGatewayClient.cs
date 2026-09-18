using System.Linq;
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
    private readonly IOptionsMonitor<EsewaOptions> _optionsMonitor;
    private readonly HttpClient _httpClient;

    private EsewaOptions Options => _optionsMonitor.CurrentValue;

    public PaymentGateway Gateway => PaymentGateway.Esewa;

    public EsewaGatewayClient(IOptionsMonitor<EsewaOptions> optionsMonitor, IHttpClientFactory httpClientFactory)
    {
        _optionsMonitor = optionsMonitor;
        _httpClient = httpClientFactory.CreateClient("Esewa");
    }

    public Task<GatewayInitiationResult> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
    {
        var totalAmount = amountNpr.ToString("F2");
        var transactionUuid = paymentId.ToString();
        var signedFieldNames = "total_amount,transaction_uuid,product_code";
        var signature = SignFields(($"total_amount={totalAmount},transaction_uuid={transactionUuid},product_code={Options.ProductCode}"));

        var fields = new Dictionary<string, string>
        {
            ["amount"] = totalAmount,
            ["tax_amount"] = "0",
            ["total_amount"] = totalAmount,
            ["transaction_uuid"] = transactionUuid,
            ["product_code"] = Options.ProductCode,
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
        return Task.FromResult(new GatewayInitiationResult(new GatewayCheckoutInfo(Options.FormUrl, fields), transactionUuid));
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
        string signMessage;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedData));
            using var document = JsonDocument.Parse(decoded);
            var root = document.RootElement;
            totalAmount = root.GetProperty("total_amount").GetString() ?? "";
            transactionUuid = root.GetProperty("transaction_uuid").GetString() ?? "";
            receivedSignature = root.GetProperty("signature").GetString() ?? "";
            transactionCode = root.TryGetProperty("transaction_code", out var codeProp) ? codeProp.GetString() : null;

            // The browser-return callback signs a DIFFERENT, and self-referential, field set than
            // the initiate request — confirmed against the real eSewa RC sandbox: the callback's
            // own signed_field_names is "transaction_code,status,total_amount,transaction_uuid,
            // product_code,signed_field_names" (six fields, including signed_field_names' own
            // value), not the three-field "total_amount,transaction_uuid,product_code" used to
            // sign the checkout form. The message to verify must always be built from exactly the
            // field names this specific payload lists, in that order — never a hardcoded list.
            var signedFieldNames = root.GetProperty("signed_field_names").GetString()
                ?? throw new JsonException("signed_field_names missing");
            signMessage = string.Join(",", signedFieldNames.Split(',').Select(name => $"{name}={root.GetProperty(name).GetString()}"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Malformed/attacker-crafted callback data (bad base64, invalid JSON, missing
            // fields) must never propagate as an unhandled exception — treat it as an
            // unverified payment.
            return new GatewayVerificationResult(false, null, "invalid payload");
        }

        var expectedSignature = SignFields(signMessage);
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
        // Numeric comparison, not string-exact: confirmed against the real eSewa RC sandbox that
        // the decoded callback's own total_amount is NOT reliably zero-padded to two decimals —
        // a genuinely-COMPLETE NPR 10.00 payment came back as "10.0", which would fail a
        // "10.0" != "10.00" string check and reject a real, already-completed payment.
        if (!decimal.TryParse(totalAmount, out var receivedAmount) || receivedAmount != expectedAmountNpr)
        {
            return new GatewayVerificationResult(false, null, decoded);
        }

        // Server-to-server confirmation — never trust the decoded redirect payload alone. The
        // decoded payload's own transaction_code (set above) is kept as GatewayTransactionId
        // here rather than whatever CallStatusCheckApiAsync's ref_id resolves to, since that is
        // the value this codebase has always surfaced for a browser-return confirmation.
        var (succeeded, _, body) = await CallStatusCheckApiAsync(transactionUuid, totalAmount, cancellationToken);
        return new GatewayVerificationResult(succeeded, transactionCode, body);
    }

    public async Task<GatewayVerificationResult> CheckStatusAsync(Guid paymentId, decimal expectedAmountNpr, string? expectedGatewayReference, CancellationToken cancellationToken = default)
    {
        // No browser return ever happened for this payment (that's the whole point of a
        // reconciliation call) — expectedGatewayReference IS the transaction_uuid to check,
        // falling back to paymentId.ToString() only because that's what InitiateAsync always
        // sets it to in the first place.
        var transactionUuid = expectedGatewayReference ?? paymentId.ToString();
        var totalAmount = expectedAmountNpr.ToString("F2");
        var (succeeded, refId, body) = await CallStatusCheckApiAsync(transactionUuid, totalAmount, cancellationToken);
        return new GatewayVerificationResult(succeeded, refId, body);
    }

    private async Task<(bool Succeeded, string? RefId, string Body)> CallStatusCheckApiAsync(string transactionUuid, string totalAmount, CancellationToken cancellationToken)
    {
        var statusUrl = $"{Options.StatusCheckUrl}?product_code={Uri.EscapeDataString(Options.ProductCode)}&total_amount={Uri.EscapeDataString(totalAmount)}&transaction_uuid={Uri.EscapeDataString(transactionUuid)}";
        var response = await _httpClient.GetAsync(statusUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, null, body);
        }

        try
        {
            using var statusDocument = JsonDocument.Parse(body);
            var root = statusDocument.RootElement;
            var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            var refId = root.TryGetProperty("ref_id", out var refProp) ? refProp.GetString() : null;
            return (status == "COMPLETE", refId, body);
        }
        catch (JsonException)
        {
            // Malformed status-check response must never propagate as an unhandled exception —
            // treat it as an unverified payment, same as every other defensive parse in this class.
            return (false, null, body);
        }
    }

    private string SignFields(string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(Options.SecretKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = new HMACSHA256(keyBytes).ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }
}
