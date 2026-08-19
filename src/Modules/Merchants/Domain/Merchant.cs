namespace Courier.Merchants.Domain;

public class Merchant
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string LegalName { get; set; }
    public required string DisplayName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public KycStatus KycStatus { get; set; } = KycStatus.Pending;
    public bool IsActive { get; set; } = true;
}
