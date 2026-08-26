using Courier.Webhooks.Application;
using Xunit;

namespace Courier.Webhooks.Tests;

public class WebhookSignatureTests
{
    [Fact]
    public void Compute_SamePayloadAndSecret_ProducesSameSignature()
    {
        var sig1 = WebhookSignature.Compute("my-secret", "{\"a\":1}");
        var sig2 = WebhookSignature.Compute("my-secret", "{\"a\":1}");
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Compute_DifferentSecret_ProducesDifferentSignature()
    {
        var sig1 = WebhookSignature.Compute("secret-one", "{\"a\":1}");
        var sig2 = WebhookSignature.Compute("secret-two", "{\"a\":1}");
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Compute_HasExpectedPrefix()
    {
        var sig = WebhookSignature.Compute("secret", "payload");
        Assert.StartsWith("sha256=", sig);
    }
}
