namespace Courier.Merchants.Domain;

public class MerchantUser
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public MerchantUserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
