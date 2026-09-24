using System.Text.Json;
using Courier.International.Application.Carriers;
using Microsoft.AspNetCore.DataProtection;

namespace Courier.International.Infrastructure.Carriers;

// Two purpose strings — Data Protection derives a distinct key per purpose, so credential
// ciphertext and webhook-secret ciphertext can never be decrypted as each other.
public sealed class DataProtectionCarrierSecretProtector : ICarrierSecretProtector
{
    private readonly IDataProtector _credentialsProtector;
    private readonly IDataProtector _webhookSecretProtector;

    public DataProtectionCarrierSecretProtector(IDataProtectionProvider provider)
    {
        _credentialsProtector = provider.CreateProtector("International.CarrierAccount.Credentials");
        _webhookSecretProtector = provider.CreateProtector("International.CarrierAccount.WebhookSecret");
    }

    public string ProtectCredentials(CarrierCredentials credentials) =>
        _credentialsProtector.Protect(JsonSerializer.Serialize(credentials.Values));

    public CarrierCredentials UnprotectCredentials(string? ciphertext)
    {
        if (ciphertext is null)
        {
            return CarrierCredentials.Empty;
        }
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(_credentialsProtector.Unprotect(ciphertext))
            ?? new Dictionary<string, string>();
        return new CarrierCredentials(values);
    }

    public string ProtectWebhookSecret(string secret) => _webhookSecretProtector.Protect(secret);

    public string UnprotectWebhookSecret(string ciphertext) => _webhookSecretProtector.Unprotect(ciphertext);
}
