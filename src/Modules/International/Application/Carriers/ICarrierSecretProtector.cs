namespace Courier.International.Application.Carriers;

public interface ICarrierSecretProtector
{
    string ProtectCredentials(CarrierCredentials credentials);
    // null ciphertext (no credentials configured, e.g. the manual carrier) -> CarrierCredentials.Empty.
    CarrierCredentials UnprotectCredentials(string? ciphertext);
    string ProtectWebhookSecret(string secret);
    string UnprotectWebhookSecret(string ciphertext);
}
