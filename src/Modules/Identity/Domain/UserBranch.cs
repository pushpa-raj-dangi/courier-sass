namespace Courier.Identity.Domain;

public class UserBranch
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid GrantedByUserId { get; set; }
}
