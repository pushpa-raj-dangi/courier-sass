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

    [Fact]
    public async Task InitiateAsync_ReturnsPaymentUrlFromResponse()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", payment_url = "https://pay.khalti.com/abc123", expires_at = "2026-09-08T00:00:00Z", expires_in = 1800 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var checkoutInfo = await client.InitiateAsync(Guid.NewGuid(), 2000m, "https://example.test/return");

        Assert.Equal("https://pay.khalti.com/abc123", checkoutInfo.RedirectUrl);
        Assert.Empty(checkoutInfo.FormFields);
    }

    [Fact]
    public async Task VerifyAsync_CompletedStatus_Succeeds()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Completed", transaction_id = "TXN456", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.True(result.Succeeded);
        Assert.Equal("TXN456", result.GatewayTransactionId);
    }

    [Fact]
    public async Task VerifyAsync_PendingStatus_Fails()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Pending", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task VerifyAsync_LookupResponseMissingStatusField_ReturnsFailureWithoutThrowing()
    {
        // No "status" property at all — the pre-fix GetProperty("status") call would throw
        // KeyNotFoundException here instead of returning a controlled failure result.
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.False(result.Succeeded);
    }
}
