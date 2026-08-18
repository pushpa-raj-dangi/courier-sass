namespace Courier.Identity.Domain;

public class Role
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string Name { get; set; }
    public bool IsSystemDefault { get; set; }
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
