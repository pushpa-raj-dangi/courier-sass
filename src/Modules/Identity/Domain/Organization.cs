namespace Courier.Identity.Domain;

public class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string SubscriptionPlan { get; set; } = "Free";
    public bool IsActive { get; set; } = true;
}
