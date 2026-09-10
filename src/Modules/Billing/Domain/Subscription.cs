namespace Courier.Billing.Domain;

// One row per organization — live operational state, not versioned/append-only like
// Pricing.RateCard (a Subscription is not "priced at a point in time," it's "which Plan
// applies right now"). Invoice is what snapshots price at a point in time.
public class Subscription
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    // Null while Status is Trial/TrialExpired — a trial has no committed Plan yet.
    public Guid? PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    // Set only when Status is Trial; read by TrialExpiryJob to decide when to flip to
    // TrialExpired. Left populated (not cleared) once a real Plan is chosen — it becomes inert,
    // matching how CurrentCycleStart/End are never retroactively cleared either.
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset CurrentCycleStart { get; set; }
    public DateTimeOffset CurrentCycleEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
