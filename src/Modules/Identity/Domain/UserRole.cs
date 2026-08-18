namespace Courier.Identity.Domain;

public class UserRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid OrganizationId { get; set; }
    public BranchScope BranchScope { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public Guid AssignedByUserId { get; set; }
}
