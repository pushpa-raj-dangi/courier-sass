namespace Courier.Billing.Application;

public class BillingValidationException : Exception
{
    public BillingValidationException(string message) : base(message) { }
}
