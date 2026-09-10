namespace Courier.Billing.Application;

public class BillingOptions
{
    public int GraceDays { get; set; } = 7;
    public int TrialDays { get; set; } = 7;
}
