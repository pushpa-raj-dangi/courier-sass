using System.Net;
using System.Text.Json;
using Courier.Billing.Infrastructure;
using Xunit;

namespace Courier.Billing.Tests;

public class KhaltiGatewayClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public StubHandler(string responseBody) { _responseBody = responseBody; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseBody) });
    }

    private static KhaltiOptions Options() => new()
    {
        SecretKey = "test-secret-key",
        InitiateUrl = "https://dev.khalti.com/api/v2/epayment/initiate/",
        LookupUrl = "https://dev.khalti.com/api/v2/epayment/lookup/",
        WebsiteUrl = "https://example.test"
    };

    // Proves VerifyAsync rejects a mismatched pidx WITHOUT ever calling Khalti's lookup API —
    // invoking this handler fails the test.
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Lookup endpoint should not have been called.");
    }

    [Fact]
    public async Task InitiateAsync_ReturnsPaymentUrlFromResponse()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", payment_url = "https://pay.khalti.com/abc123", expires_at = "2026-09-08T00:00:00Z", expires_in = 1800 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.InitiateAsync(Guid.NewGuid(), 2000m, "https://example.test/return");

        Assert.Equal("https://pay.khalti.com/abc123", result.CheckoutInfo.RedirectUrl);
        Assert.Empty(result.CheckoutInfo.FormFields);
        Assert.Equal("abc123", result.GatewayReference);
    }

    [Fact]
    public async Task VerifyAsync_CompletedStatusAndMatchingAmountAndReference_Succeeds()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Completed", transaction_id = "TXN456", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), 2000m, "abc123", new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.True(result.Succeeded);
        Assert.Equal("TXN456", result.GatewayTransactionId);
    }

    [Fact]
    public async Task VerifyAsync_PendingStatus_Fails()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Pending", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), 2000m, "abc123", new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_LookupResponseMissingStatusField_ReturnsFailureWithoutThrowing()
    {
        // No "status" property at all — the pre-fix GetProperty("status") call would throw
        // KeyNotFoundException here instead of returning a controlled failure result.
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), 2000m, "abc123", new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.False(result.Succeeded);
    }

    // Critical regression test: a callback whose pidx does not match the payment's own persisted
    // GatewayReference must be rejected WITHOUT even calling Khalti's lookup API — this is what
    // closes the cross-payment confirmation vulnerability (a genuinely-"Completed" pidx that
    // belongs to a DIFFERENT payment must never confirm this one).
    [Fact]
    public async Task VerifyAsync_PidxDoesNotMatchExpectedGatewayReference_FailsWithoutCallingLookup()
    {
        var httpClientFactory = new StubHttpClientFactory(new ThrowingHandler());
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), 2000m, "expected-pidx-for-this-payment", new Dictionary<string, string> { ["pidx"] = "some-other-payments-pidx" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CheckStatusAsync_CompletedStatus_SucceedsUsingPersistedGatewayReferenceAlone()
    {
        // No callbackParameters dict at all — reconciliation only ever has the persisted
        // GatewayReference (pidx) and the payment's own expected amount.
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Completed", transaction_id = "TXN456", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.CheckStatusAsync(Guid.NewGuid(), 2000m, "abc123");

        Assert.True(result.Succeeded);
        Assert.Equal("TXN456", result.GatewayTransactionId);
    }

    [Fact]
    public async Task CheckStatusAsync_NoGatewayReference_FailsWithoutCallingLookup()
    {
        var httpClientFactory = new StubHttpClientFactory(new ThrowingHandler());
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.CheckStatusAsync(Guid.NewGuid(), 2000m, null);

        Assert.False(result.Succeeded);
    }

    // A matching pidx but a lookup response reporting a DIFFERENT total_amount than what this
    // payment actually costs must also be rejected — otherwise a payment for a smaller declared
    // amount could be looked up successfully and treated as full confirmation.
    [Fact]
    public async Task VerifyAsync_MatchingPidxButMismatchedAmount_Fails()
    {
        // Lookup reports total_amount = 50000 paisa (NPR 500.00), but the payment being confirmed
        // is expected to cost NPR 2000.00 (200000 paisa).
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Completed", transaction_id = "TXN456", total_amount = 50000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), 2000m, "abc123", new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.False(result.Succeeded);
    }
}
