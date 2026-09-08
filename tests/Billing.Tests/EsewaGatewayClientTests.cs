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

    private static EsewaOptions Options() => new()
    {
        ProductCode = "EPAYTEST",
        SecretKey = "8gBm/:&EnhH.1/q",
        FormUrl = "https://rc-epay.esewa.com.np/api/epay/main/v2/form",
        StatusCheckUrl = "https://rc.esewa.com.np/api/epay/transaction/status/"
    };

    [Fact]
    public async Task InitiateAsync_ReturnsSignedFormFields()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{}"));
        var client = new EsewaGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var checkoutInfo = await client.InitiateAsync(Guid.NewGuid(), 2000m, "https://example.test/return");

        Assert.Equal("EPAYTEST", checkoutInfo.FormFields["product_code"]);
        Assert.False(string.IsNullOrEmpty(checkoutInfo.FormFields["signature"]));
    }

    [Fact]
    public async Task VerifyAsync_ValidSignatureAndCompleteStatus_Succeeds()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var totalAmount = "2000.00";
        var signMessage = $"total_amount={totalAmount},transaction_uuid={paymentId},product_code={options.ProductCode}";
        var signature = Convert.ToBase64String(new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey)).ComputeHash(Encoding.UTF8.GetBytes(signMessage)));
        var payload = JsonSerializer.Serialize(new { total_amount = totalAmount, transaction_uuid = paymentId.ToString(), product_code = options.ProductCode, signature, transaction_code = "TXN123" });
        var callbackParameters = new Dictionary<string, string> { ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) };

        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"COMPLETE\"}"));
        var client = new EsewaGatewayClient(Microsoft.Extensions.Options.Options.Create(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, callbackParameters);

        Assert.True(result.Succeeded);
        Assert.Equal("TXN123", result.GatewayTransactionId);
    }
}

// Shared across both gateway test files (Task 4 Step 8 references this same class).
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
    public HttpClient CreateClient(string name) => new(_handler);
}
