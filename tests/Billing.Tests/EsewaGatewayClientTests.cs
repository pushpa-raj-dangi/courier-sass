using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Courier.Billing.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Billing.Tests;

public class EsewaGatewayClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public StubHandler(string responseBody) { _responseBody = responseBody; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseBody) });
    }

    // Used to prove the signature-mismatch/malformed-payload paths return before ever reaching
    // the server-to-server status-check call — invoking this handler fails the test.
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Status-check endpoint should not have been called.");
    }

    private static EsewaOptions Options() => new()
    {
        ProductCode = "EPAYTEST",
        SecretKey = "8gBm/:&EnhH.1/q",
        FormUrl = "https://rc-epay.esewa.com.np/api/epay/main/v2/form",
        StatusCheckUrl = "https://rc.esewa.com.np/api/epay/transaction/status/"
    };

    // Mirrors the REAL eSewa RC sandbox browser-return payload shape exactly (confirmed via a
    // live end-to-end browser test): six fields — transaction_code, status, total_amount,
    // transaction_uuid, product_code, signed_field_names — signed in that order, with
    // signed_field_names' value being its own comma-separated field list. This is a DIFFERENT
    // field set than the three-field message ("total_amount,transaction_uuid,product_code") used
    // to sign the checkout form at initiate time — VerifyAsync must build its message from
    // whatever signed_field_names the payload itself lists, never a hardcoded set.
    private static string BuildEncodedCallbackData(EsewaOptions options, string transactionCode, string status, string totalAmount, string transactionUuid, string signedFieldNamesOverride = "transaction_code,status,total_amount,transaction_uuid,product_code,signed_field_names", string? signatureOverride = null)
    {
        var fieldValues = new Dictionary<string, string>
        {
            ["transaction_code"] = transactionCode,
            ["status"] = status,
            ["total_amount"] = totalAmount,
            ["transaction_uuid"] = transactionUuid,
            ["product_code"] = options.ProductCode,
            ["signed_field_names"] = signedFieldNamesOverride
        };
        var signMessage = string.Join(",", signedFieldNamesOverride.Split(',').Select(name => $"{name}={fieldValues[name]}"));
        var signature = signatureOverride ?? Convert.ToBase64String(new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey)).ComputeHash(Encoding.UTF8.GetBytes(signMessage)));
        var payload = JsonSerializer.Serialize(new
        {
            transaction_code = transactionCode,
            status,
            total_amount = totalAmount,
            transaction_uuid = transactionUuid,
            product_code = options.ProductCode,
            signed_field_names = signedFieldNamesOverride,
            signature
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    [Fact]
    public async Task InitiateAsync_ReturnsSignedFormFields()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{}"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(Options()), httpClientFactory);

        var paymentId = Guid.NewGuid();
        var result = await client.InitiateAsync(paymentId, 2000m, "https://example.test/return");

        Assert.Equal("EPAYTEST", result.CheckoutInfo.FormFields["product_code"]);
        Assert.False(string.IsNullOrEmpty(result.CheckoutInfo.FormFields["signature"]));
        Assert.Equal(paymentId.ToString(), result.GatewayReference);
    }

    [Fact]
    public async Task VerifyAsync_ValidSignatureAndCompleteStatus_Succeeds()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var encodedData = BuildEncodedCallbackData(options, "TXN123", "COMPLETE", "2000.00", paymentId.ToString());
        var callbackParameters = new Dictionary<string, string> { ["data"] = encodedData };

        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"COMPLETE\"}"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 2000m, paymentId.ToString(), callbackParameters);

        Assert.True(result.Succeeded);
        Assert.Equal("TXN123", result.GatewayTransactionId);
    }

    // Regression test for a real bug caught against the live eSewa RC sandbox: a genuinely
    // COMPLETE NPR 10.00 payment's decoded callback came back with total_amount "10.0" (not
    // zero-padded to two decimals), which a "10.0" != "10.00" string comparison rejected as
    // unverified even though the payment had actually gone through. The comparison must be
    // numeric.
    [Fact]
    public async Task VerifyAsync_DecodedTotalAmountNotZeroPadded_StillMatchesNumerically()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var encodedData = BuildEncodedCallbackData(options, "TXN123", "COMPLETE", "10.0", paymentId.ToString());
        var callbackParameters = new Dictionary<string, string> { ["data"] = encodedData };

        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"COMPLETE\"}"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 10.00m, paymentId.ToString(), callbackParameters);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_SignatureMismatch_FailsWithoutCallingStatusCheck()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        // Deliberately wrong signature — not derived from the HMAC of the payload's own listed fields.
        var encodedData = BuildEncodedCallbackData(options, "TXN123", "COMPLETE", "2000.00", paymentId.ToString(), signatureOverride: "this-is-not-the-real-signature");
        var callbackParameters = new Dictionary<string, string> { ["data"] = encodedData };

        // ThrowingHandler proves VerifyAsync short-circuits on signature mismatch instead of
        // proceeding to the server-to-server status-check call.
        var httpClientFactory = new StubHttpClientFactory(new ThrowingHandler());
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 2000m, paymentId.ToString(), callbackParameters);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_ValidSignatureButPendingStatus_Fails()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var encodedData = BuildEncodedCallbackData(options, "TXN123", "COMPLETE", "2000.00", paymentId.ToString());
        var callbackParameters = new Dictionary<string, string> { ["data"] = encodedData };

        // Signature is valid, but the server-to-server status-check reports the transaction is
        // still pending — VerifyAsync must not treat a valid signature alone as success.
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"PENDING\"}"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 2000m, paymentId.ToString(), callbackParameters);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_ValidSignatureButMismatchedExpectedAmount_FailsWithoutCallingStatusCheck()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var encodedData = BuildEncodedCallbackData(options, "TXN123", "COMPLETE", "2000.00", paymentId.ToString());
        var callbackParameters = new Dictionary<string, string> { ["data"] = encodedData };

        // The callback's own total_amount (2000.00) is validly signed, but the payment being
        // confirmed is expected to cost NPR 50000.00 — must fail before ever hitting the
        // server-to-server status-check call.
        var httpClientFactory = new StubHttpClientFactory(new ThrowingHandler());
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 50000m, paymentId.ToString(), callbackParameters);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_MissingSignedFieldNames_ReturnsFailureWithoutThrowing()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        // No signed_field_names property at all — an older/malformed payload shape must never
        // crash VerifyAsync with an unhandled KeyNotFoundException.
        var payload = JsonSerializer.Serialize(new { total_amount = "2000.00", transaction_uuid = paymentId.ToString(), product_code = options.ProductCode, signature = "irrelevant" });
        var callbackParameters = new Dictionary<string, string> { ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) };

        var httpClientFactory = new StubHttpClientFactory(new ThrowingHandler());
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 2000m, paymentId.ToString(), callbackParameters);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CheckStatusAsync_CompleteStatus_SucceedsUsingPersistedGatewayReferenceAlone()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        // No browser callback data at all — reconciliation only ever has the persisted
        // GatewayReference (== transaction_uuid) and the payment's own expected amount.
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"COMPLETE\",\"ref_id\":\"REF789\"}"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.CheckStatusAsync(paymentId, 2000m, paymentId.ToString());

        Assert.True(result.Succeeded);
        Assert.Equal("REF789", result.GatewayTransactionId);
    }

    [Fact]
    public async Task CheckStatusAsync_PendingStatus_Fails()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"PENDING\"}"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.CheckStatusAsync(paymentId, 2000m, paymentId.ToString());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CheckStatusAsync_MalformedResponseBody_ReturnsFailureWithoutThrowing()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("not json at all"));
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.CheckStatusAsync(paymentId, 2000m, paymentId.ToString());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_MalformedCallbackData_ReturnsFailureWithoutThrowing()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var callbackParameters = new Dictionary<string, string> { ["data"] = "not-valid-base64!!!" };

        var httpClientFactory = new StubHttpClientFactory(new ThrowingHandler());
        var client = new EsewaGatewayClient(new StaticOptionsMonitor<EsewaOptions>(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, 2000m, paymentId.ToString(), callbackParameters);

        Assert.False(result.Succeeded);
    }
}

// Shared across both gateway test files (Task 4 Step 8 references this same class).
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
    public HttpClient CreateClient(string name) => new(_handler);
}
