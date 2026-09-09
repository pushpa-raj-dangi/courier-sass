using System.Text.Json.Serialization;

namespace Courier.Billing.Domain;

// Same fix as PaymentGateway.cs — without this, PlanSummary.Status (exposed to the frontend's
// Billing page as "Active"/"PastDue"/"Cancelled") serializes as a raw integer instead.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Cancelled = 2
}
