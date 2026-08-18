namespace Courier.Identity.Domain;

public class User
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
