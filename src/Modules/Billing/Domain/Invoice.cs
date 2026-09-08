namespace Courier.Billing.Domain;

// PlanNameSnapshot/AmountNprSnapshot are copied at generation time — a later Plan price change
// never alters an already-generated Invoice (same principle as Shipment's locked-in pricing
// and RateCard versioning elsewhere in this codebase).
public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid PlanId { get; set; }
    public required string PlanNameSnapshot { get; set; }
    public decimal AmountNprSnapshot { get; set; }
    public DateTimeOffset CycleStart { get; set; }
    public DateTimeOffset CycleEnd { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
