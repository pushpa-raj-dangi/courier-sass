using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Microsoft.AspNetCore.DataProtection;

namespace Courier.International.Tests.Carriers;

public class DataProtectionCarrierSecretProtectorTests
{
    private readonly DataProtectionCarrierSecretProtector _protector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Credentials_RoundTrip_AndCiphertextDoesNotContainPlaintext()
    {
        var credentials = new CarrierCredentials(new Dictionary<string, string> { ["apiKey"] = "sk_live_12345", ["account"] = "ACC-9" });

        var ciphertext = _protector.ProtectCredentials(credentials);
        var roundTripped = _protector.UnprotectCredentials(ciphertext);

        Assert.DoesNotContain("sk_live_12345", ciphertext);
        Assert.Equal("sk_live_12345", roundTripped.Values["apiKey"]);
        Assert.Equal("ACC-9", roundTripped.Values["account"]);
    }

    [Fact]
    public void UnprotectCredentials_Null_IsEmpty()
    {
        Assert.Empty(_protector.UnprotectCredentials(null).Values);
    }

    [Fact]
    public void WebhookSecret_UsesADifferentPurposeThanCredentials()
    {
        var ciphertext = _protector.ProtectWebhookSecret("whsec_abc");

        Assert.Equal("whsec_abc", _protector.UnprotectWebhookSecret(ciphertext));
        Assert.ThrowsAny<Exception>(() => _protector.UnprotectCredentials(ciphertext));
    }

    [Fact]
    public void CredentialsToString_DoesNotLeakValues()
    {
        var credentials = new CarrierCredentials(new Dictionary<string, string> { ["apiKey"] = "sk_live_12345" });

        Assert.DoesNotContain("sk_live_12345", credentials.ToString());
    }
}
