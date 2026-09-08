namespace Courier.Billing.Infrastructure;

public class KhaltiOptions
{
    public required string SecretKey { get; set; }
    public required string InitiateUrl { get; set; }
    public required string LookupUrl { get; set; }
    public required string WebsiteUrl { get; set; }
}
