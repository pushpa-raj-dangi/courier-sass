namespace Courier.Billing.Infrastructure;

public class EsewaOptions
{
    public required string ProductCode { get; set; }
    public required string SecretKey { get; set; }
    public required string FormUrl { get; set; }
    public required string StatusCheckUrl { get; set; }
}
