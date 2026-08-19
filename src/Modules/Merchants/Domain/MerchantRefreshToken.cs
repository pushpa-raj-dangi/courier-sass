namespace Courier.Merchants.Domain;

public class MerchantRefreshToken
{
    public Guid Id { get; set; }
    public Guid MerchantUserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
