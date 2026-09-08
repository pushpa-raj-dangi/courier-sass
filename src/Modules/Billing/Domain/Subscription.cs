namespace Courier.Billing.Domain;

// One row per organization — live operational state, not versioned/append-only like
// Pricing.RateCard (a Subscription is not "priced at a point in time," it's "which Plan
// applies right now"). Invoice (Task 3) is what snapshots price at a point in time.
public class Subscription
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset CurrentCycleStart { get; set; }
    public DateTimeOffset CurrentCycleEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
