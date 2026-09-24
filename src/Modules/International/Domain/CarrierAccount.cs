namespace Courier.International.Domain;

// A tenant's configured account with one partner carrier (I4 spec §4.1). Several accounts per
// carrier are allowed. Secrets are Data-Protection ciphertext, never plaintext; the manual carrier
// has none. Never deleted once a booking references it — deactivated instead.
public class CarrierAccount
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string CarrierCode { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? EncryptedCredentials { get; set; }
    public string? EncryptedWebhookSecret { get; set; }
    // Random, unguessable, platform-unique — the only identifier in the public webhook URL.
    public required string WebhookPublicId { get; set; }
    public bool PollingEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
