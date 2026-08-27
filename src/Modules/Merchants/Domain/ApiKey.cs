namespace Courier.Merchants.Domain;

// HashedKey stores SHA-256(secret) — never the plaintext. The plaintext (formatted
// "{Prefix}.{secret}") is shown exactly once, in the POST /api/v1/merchant/api-keys response
// body, same one-time-reveal pattern as WebhookSubscription.Secret. Prefix is stored separately,
// unhashed, so ApiKeyAuthenticationHandler (Task 5) can look up a candidate key by prefix in
// O(1) before it ever hashes and compares the full secret.
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string HashedKey { get; set; }
    public required string Prefix { get; set; }
    public required string[] Scopes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
