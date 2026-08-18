namespace Courier.Identity.Domain;

public class Permission
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Description { get; set; }
}
